using Microsoft.Extensions.Logging;
using Npgsql;

namespace Northwoods.Tenancy;

/// <summary>
/// Runs idempotent schema creation, index setup, and seed data on startup.
/// Replaces the Docker-only init.sql entrypoint mechanism so the API is
/// self-initializing against any Postgres instance (Render, local, CI).
/// </summary>
public static class DatabaseInitializer
{
    /// <summary>
    /// Ensures the database schema, indexes, and seed data exist.
    /// Safe to call on every startup — all statements are idempotent.
    /// </summary>
    public static async Task InitializeAsync(string connectionString, bool setupRls, ILogger logger)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();

            logger.LogInformation("DatabaseInitializer: running schema initialization");

            // Phase 1: Extensions (may require owner privileges on managed Postgres)
            await ExecuteSqlSafe(conn, ExtensionsSql, logger, "extensions");

            // Phase 2: Tables + Indexes (all IF NOT EXISTS)
            await ExecuteSqlSafe(conn, CoreTablesSql, logger, "core tables");
            await ExecuteSqlSafe(conn, VectorTablesSql, logger, "vector tables");
            await ExecuteSqlSafe(conn, IndexesSql, logger, "indexes");

            // Phase 3: RLS setup (optional — skip on managed Postgres without app_user role)
            if (setupRls)
            {
                await ExecuteSqlSafe(conn, RoleSetupSql, logger, "app_user role");
                await ExecuteSqlSafe(conn, GrantsSql, logger, "grants");
                await ExecuteSqlSafe(conn, RlsPoliciesSql, logger, "RLS policies");
            }
            else
            {
                logger.LogInformation("DatabaseInitializer: skipping RLS/role setup (UseAppUserRole=false)");
            }

            // Phase 3b: Schema migrations (safe to re-run)
            await ExecuteSqlSafe(conn, MigrationsSql, logger, "migrations");

            // Phase 4: Seed data (ON CONFLICT — idempotent)
            await ExecuteSqlSafe(conn, SeedTenantsSql, logger, "seed tenants");
            await ExecuteSqlSafe(conn, SeedUsersSql, logger, "seed users");
            await ExecuteSqlSafe(conn, SeedTemplatesSql, logger, "seed templates");
            await ExecuteSqlSafe(conn, SeedDocumentsSql, logger, "seed documents");

            logger.LogInformation("DatabaseInitializer: initialization complete");
        }
        catch (Exception ex)
        {
            // Log but don't prevent startup — the app may still serve requests
            // if the schema was already initialized by a previous run.
            logger.LogError(ex, "DatabaseInitializer: initialization failed");
        }
    }

    private static async Task ExecuteSqlAsync(NpgsqlConnection conn, string sql, ILogger logger, string label)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.CommandTimeout = 30;
        await cmd.ExecuteNonQueryAsync();
        logger.LogInformation("DatabaseInitializer: {Label} applied", label);
    }

    /// <summary>
    /// Executes SQL that may fail on managed Postgres (e.g., CREATE ROLE).
    /// Logs warning and continues on failure.
    /// </summary>
    private static async Task ExecuteSqlSafe(NpgsqlConnection conn, string sql, ILogger logger, string label)
    {
        try
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.CommandTimeout = 30;
            await cmd.ExecuteNonQueryAsync();
            logger.LogInformation("DatabaseInitializer: {Label} applied", label);
        }
        catch (PostgresException ex)
        {
            logger.LogWarning("DatabaseInitializer: {Label} skipped ({Code}: {Message})", label, ex.SqlState, ex.MessageText);
        }
    }

    // -------------------------------------------------------------------------
    // SQL Sections — extracted from infra/postgres/init.sql
    // All statements use IF NOT EXISTS / ON CONFLICT for idempotency.
    // -------------------------------------------------------------------------

    private const string MigrationsSql = """
        -- M001: Add Admin role to users check constraint (was missing in early schema)
        DO $$ BEGIN
            ALTER TABLE users DROP CONSTRAINT IF EXISTS users_role_check;
            ALTER TABLE users ADD CONSTRAINT users_role_check
                CHECK (role IN ('IntakeWorker', 'Reviewer', 'Admin'));
        END $$;

        -- M002: Add columns that were added after initial schema creation
        ALTER TABLE templates ADD COLUMN IF NOT EXISTS is_archived BOOLEAN NOT NULL DEFAULT false;
        ALTER TABLE templates ADD COLUMN IF NOT EXISTS blank_pdf_key TEXT;
        ALTER TABLE templates ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ DEFAULT now();

        -- M003: Add extraction_attempts columns that may be missing
        ALTER TABLE extraction_attempts ADD COLUMN IF NOT EXISTS stage TEXT NOT NULL DEFAULT 'extract';
        ALTER TABLE extraction_attempts ADD COLUMN IF NOT EXISTS technique TEXT NOT NULL DEFAULT 'unknown';
        ALTER TABLE extraction_attempts ADD COLUMN IF NOT EXISTS normalized_value TEXT;
        ALTER TABLE extraction_attempts ADD COLUMN IF NOT EXISTS normalized_confidence DECIMAL(5, 4) NOT NULL DEFAULT 0;
        ALTER TABLE extraction_attempts ADD COLUMN IF NOT EXISTS requires_review BOOLEAN NOT NULL DEFAULT false;
        ALTER TABLE extraction_attempts ADD COLUMN IF NOT EXISTS details JSONB;
        """;

    private const string ExtensionsSql = """
        CREATE EXTENSION IF NOT EXISTS vector;
        CREATE EXTENSION IF NOT EXISTS pg_trgm;
        """;

    private const string CoreTablesSql = """
        CREATE TABLE IF NOT EXISTS tenants (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            created_at TIMESTAMPTZ DEFAULT now()
        );

        CREATE TABLE IF NOT EXISTS users (
            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            tenant_id TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
            email TEXT NOT NULL,
            password_hash TEXT NOT NULL,
            role TEXT NOT NULL CHECK (role IN ('IntakeWorker', 'Reviewer', 'Admin')),
            created_at TIMESTAMPTZ DEFAULT now(),
            UNIQUE(tenant_id, email)
        );

        CREATE TABLE IF NOT EXISTS templates (
            id TEXT NOT NULL,
            tenant_id TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
            name TEXT NOT NULL,
            field_schema JSONB NOT NULL,
            is_archived BOOLEAN NOT NULL DEFAULT false,
            blank_pdf_key TEXT,
            created_at TIMESTAMPTZ DEFAULT now(),
            updated_at TIMESTAMPTZ DEFAULT now(),
            PRIMARY KEY (id, tenant_id)
        );

        CREATE TABLE IF NOT EXISTS documents (
            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            tenant_id TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
            template_id TEXT NOT NULL,
            uploaded_by UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
            original_file_key TEXT NOT NULL,
            original_file_name TEXT NOT NULL,
            status TEXT NOT NULL DEFAULT 'uploaded' CHECK (status IN ('uploaded', 'extracting', 'review_ready', 'completed', 'finalized', 'failed')),
            created_at TIMESTAMPTZ DEFAULT now(),
            updated_at TIMESTAMPTZ DEFAULT now()
        );

        CREATE TABLE IF NOT EXISTS extracted_fields (
            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
            tenant_id TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
            field_key TEXT NOT NULL,
            extracted_value TEXT,
            corrected_value TEXT,
            confidence DECIMAL(5, 4) NOT NULL,
            requires_review BOOLEAN NOT NULL DEFAULT false,
            created_at TIMESTAMPTZ DEFAULT now(),
            updated_at TIMESTAMPTZ DEFAULT now(),
            UNIQUE(document_id, field_key)
        );

        CREATE TABLE IF NOT EXISTS extraction_attempts (
            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
            tenant_id TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
            extraction_run_id UUID NOT NULL,
            field_key TEXT NOT NULL,
            provider TEXT NOT NULL,
            stage TEXT NOT NULL,
            technique TEXT NOT NULL,
            raw_value TEXT,
            raw_confidence DECIMAL(5, 4) NOT NULL,
            normalized_value TEXT,
            normalized_confidence DECIMAL(5, 4) NOT NULL,
            requires_review BOOLEAN NOT NULL DEFAULT false,
            details JSONB,
            created_at TIMESTAMPTZ DEFAULT now()
        );

        CREATE TABLE IF NOT EXISTS audit_events (
            id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
            document_id UUID REFERENCES documents(id) ON DELETE CASCADE,
            tenant_id TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
            event_type TEXT NOT NULL,
            details JSONB,
            actor_id UUID REFERENCES users(id) ON DELETE SET NULL,
            created_at TIMESTAMPTZ DEFAULT now()
        );
        """;

    // Separated because it depends on the vector extension which may not be available
    private const string VectorTablesSql = """
        CREATE TABLE IF NOT EXISTS case_profiles (
            document_id UUID PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
            tenant_id TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
            template_id TEXT NOT NULL,
            applicant_name TEXT,
            date_of_birth TEXT,
            address TEXT,
            search_text TEXT NOT NULL,
            search_tsv TSVECTOR GENERATED ALWAYS AS (to_tsvector('simple', COALESCE(search_text, ''))) STORED,
            embedding VECTOR(1536),
            created_at TIMESTAMPTZ DEFAULT now(),
            updated_at TIMESTAMPTZ DEFAULT now()
        );
        """;

    private const string IndexesSql = """
        CREATE INDEX IF NOT EXISTS idx_users_tenant_id ON users(tenant_id);
        CREATE INDEX IF NOT EXISTS idx_templates_tenant_id ON templates(tenant_id);
        CREATE INDEX IF NOT EXISTS idx_documents_tenant_id ON documents(tenant_id);
        CREATE INDEX IF NOT EXISTS idx_documents_template_id ON documents(template_id);
        CREATE INDEX IF NOT EXISTS idx_documents_uploaded_by ON documents(uploaded_by);
        CREATE INDEX IF NOT EXISTS idx_extracted_fields_tenant_id ON extracted_fields(tenant_id);
        CREATE INDEX IF NOT EXISTS idx_extracted_fields_document_id ON extracted_fields(document_id);
        CREATE INDEX IF NOT EXISTS idx_extracted_fields_value_trgm ON extracted_fields USING GIN(extracted_value gin_trgm_ops);
        CREATE INDEX IF NOT EXISTS idx_case_profiles_tenant_id ON case_profiles(tenant_id);
        CREATE INDEX IF NOT EXISTS idx_case_profiles_template_id ON case_profiles(template_id);
        CREATE INDEX IF NOT EXISTS idx_case_profiles_search_tsv ON case_profiles USING GIN(search_tsv);
        CREATE INDEX IF NOT EXISTS idx_case_profiles_applicant_trgm ON case_profiles USING GIN(applicant_name gin_trgm_ops);
        CREATE INDEX IF NOT EXISTS idx_case_profiles_address_trgm ON case_profiles USING GIN(address gin_trgm_ops);
        CREATE INDEX IF NOT EXISTS idx_case_profiles_dob ON case_profiles(date_of_birth);
        CREATE INDEX IF NOT EXISTS idx_extraction_attempts_document_id ON extraction_attempts(document_id);
        CREATE INDEX IF NOT EXISTS idx_extraction_attempts_tenant_id ON extraction_attempts(tenant_id);
        CREATE INDEX IF NOT EXISTS idx_audit_events_tenant_id ON audit_events(tenant_id);
        CREATE INDEX IF NOT EXISTS idx_audit_events_document_id ON audit_events(document_id);
        """;

    private const string RoleSetupSql = """
        DO $$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_user') THEN
                CREATE ROLE app_user WITH LOGIN PASSWORD 'app_user';
            END IF;
        END
        $$;
        """;

    private const string GrantsSql = """
        GRANT SELECT, INSERT, UPDATE, DELETE ON tenants TO app_user;
        GRANT SELECT, INSERT, UPDATE, DELETE ON users TO app_user;
        GRANT SELECT, INSERT, UPDATE, DELETE ON templates TO app_user;
        GRANT SELECT, INSERT, UPDATE, DELETE ON documents TO app_user;
        GRANT SELECT, INSERT, UPDATE, DELETE ON extracted_fields TO app_user;
        GRANT SELECT, INSERT, UPDATE, DELETE ON case_profiles TO app_user;
        GRANT SELECT, INSERT, UPDATE, DELETE ON extraction_attempts TO app_user;
        GRANT SELECT, INSERT, UPDATE, DELETE ON audit_events TO app_user;
        GRANT USAGE ON ALL SEQUENCES IN SCHEMA public TO app_user;
        """;

    private const string RlsPoliciesSql = """
        ALTER TABLE users ENABLE ROW LEVEL SECURITY;
        ALTER TABLE templates ENABLE ROW LEVEL SECURITY;
        ALTER TABLE documents ENABLE ROW LEVEL SECURITY;
        ALTER TABLE extracted_fields ENABLE ROW LEVEL SECURITY;
        ALTER TABLE case_profiles ENABLE ROW LEVEL SECURITY;
        ALTER TABLE audit_events ENABLE ROW LEVEL SECURITY;
        ALTER TABLE extraction_attempts ENABLE ROW LEVEL SECURITY;

        DO $$ BEGIN
            CREATE POLICY users_tenant_isolation ON users
                USING (tenant_id = current_setting('app.tenant_id', true))
                WITH CHECK (tenant_id = current_setting('app.tenant_id', true));
        EXCEPTION WHEN duplicate_object THEN NULL;
        END $$;

        DO $$ BEGIN
            CREATE POLICY templates_tenant_isolation ON templates
                USING (tenant_id = current_setting('app.tenant_id', true))
                WITH CHECK (tenant_id = current_setting('app.tenant_id', true));
        EXCEPTION WHEN duplicate_object THEN NULL;
        END $$;

        DO $$ BEGIN
            CREATE POLICY documents_tenant_isolation ON documents
                USING (tenant_id = current_setting('app.tenant_id', true))
                WITH CHECK (tenant_id = current_setting('app.tenant_id', true));
        EXCEPTION WHEN duplicate_object THEN NULL;
        END $$;

        DO $$ BEGIN
            CREATE POLICY extracted_fields_tenant_isolation ON extracted_fields
                USING (tenant_id = current_setting('app.tenant_id', true))
                WITH CHECK (tenant_id = current_setting('app.tenant_id', true));
        EXCEPTION WHEN duplicate_object THEN NULL;
        END $$;

        DO $$ BEGIN
            CREATE POLICY case_profiles_tenant_isolation ON case_profiles
                USING (tenant_id = current_setting('app.tenant_id', true))
                WITH CHECK (tenant_id = current_setting('app.tenant_id', true));
        EXCEPTION WHEN duplicate_object THEN NULL;
        END $$;

        DO $$ BEGIN
            CREATE POLICY extraction_attempts_tenant_isolation ON extraction_attempts
                USING (tenant_id = current_setting('app.tenant_id', true))
                WITH CHECK (tenant_id = current_setting('app.tenant_id', true));
        EXCEPTION WHEN duplicate_object THEN NULL;
        END $$;

        DO $$ BEGIN
            CREATE POLICY audit_events_tenant_isolation ON audit_events
                USING (tenant_id = current_setting('app.tenant_id', true))
                WITH CHECK (tenant_id = current_setting('app.tenant_id', true));
        EXCEPTION WHEN duplicate_object THEN NULL;
        END $$;
        """;

    private const string SeedTenantsSql = """
        INSERT INTO tenants (id, name) VALUES
            ('tenant-a', 'Sunrise County DHS'),
            ('tenant-b', 'Lakewood Family Services')
        ON CONFLICT (id) DO NOTHING;
        """;

    private const string SeedUsersSql = """
        INSERT INTO users (tenant_id, email, password_hash, role) VALUES
            ('tenant-a', 'worker@sunrise.example', '$2a$12$S.9UQ5kYJy1e7DJ/f29XnOwGKrhVCo51W2rQ.NENXd.Zo.PHWoEai', 'IntakeWorker'),
            ('tenant-a', 'reviewer@sunrise.example', '$2a$12$S.9UQ5kYJy1e7DJ/f29XnOwGKrhVCo51W2rQ.NENXd.Zo.PHWoEai', 'Reviewer'),
            ('tenant-a', 'admin@sunrise.example', '$2a$12$S.9UQ5kYJy1e7DJ/f29XnOwGKrhVCo51W2rQ.NENXd.Zo.PHWoEai', 'Admin'),
            ('tenant-b', 'worker@lakewood.example', '$2a$12$S.9UQ5kYJy1e7DJ/f29XnOwGKrhVCo51W2rQ.NENXd.Zo.PHWoEai', 'IntakeWorker'),
            ('tenant-b', 'reviewer@lakewood.example', '$2a$12$S.9UQ5kYJy1e7DJ/f29XnOwGKrhVCo51W2rQ.NENXd.Zo.PHWoEai', 'Reviewer'),
            ('tenant-b', 'admin@lakewood.example', '$2a$12$S.9UQ5kYJy1e7DJ/f29XnOwGKrhVCo51W2rQ.NENXd.Zo.PHWoEai', 'Admin')
        ON CONFLICT (tenant_id, email) DO UPDATE SET password_hash = EXCLUDED.password_hash;
        """;

    private const string SeedTemplatesSql = """
        INSERT INTO templates (id, tenant_id, name, field_schema) VALUES
            ('general-assistance', 'tenant-a', 'General Assistance Intake',
                '{"fields": [
                    {"key": "applicantName", "type": "string", "required": true},
                    {"key": "dateOfBirth", "type": "date", "required": true},
                    {"key": "address", "type": "string", "required": true},
                    {"key": "householdSize", "type": "integer", "required": true},
                    {"key": "monthlyIncome", "type": "decimal", "required": true},
                    {"key": "requestedServices", "type": "array", "required": true},
                    {"key": "notes", "type": "string", "required": false}
                ]}'::JSONB),
            ('housing-stability', 'tenant-a', 'Housing Stability Intake',
                '{"fields": [
                    {"key": "applicantName", "type": "string", "required": true},
                    {"key": "dateOfBirth", "type": "date", "required": true},
                    {"key": "currentAddress", "type": "string", "required": true},
                    {"key": "householdMembers", "type": "integer", "required": true},
                    {"key": "monthlyRent", "type": "decimal", "required": true},
                    {"key": "evictionRiskLevel", "type": "string", "required": false},
                    {"key": "requestedAssistance", "type": "string", "required": true}
                ]}'::JSONB),
            ('behavioral-health', 'tenant-a', 'Behavioral Health Intake',
                '{"fields": [
                    {"key": "clientName", "type": "string", "required": true},
                    {"key": "dateOfBirth", "type": "date", "required": true},
                    {"key": "presentingConcern", "type": "string", "required": true},
                    {"key": "currentMedications", "type": "string", "required": false},
                    {"key": "substanceUse", "type": "string", "required": false},
                    {"key": "traumaHistory", "type": "string", "required": false},
                    {"key": "suicidalIdeation", "type": "string", "required": false}
                ]}'::JSONB),
            ('soap-note', 'tenant-a', 'SOAP Progress Note',
                '{"fields": [
                    {"key": "clientName", "type": "string", "required": true},
                    {"key": "sessionNumber", "type": "string", "required": true},
                    {"key": "subjective", "type": "string", "required": true},
                    {"key": "objective", "type": "string", "required": false},
                    {"key": "assessment", "type": "string", "required": false},
                    {"key": "plan", "type": "string", "required": false},
                    {"key": "riskLevel", "type": "string", "required": true}
                ]}'::JSONB),
            ('general-assistance', 'tenant-b', 'General Assistance Intake',
                '{"fields": [
                    {"key": "applicantName", "type": "string", "required": true},
                    {"key": "dateOfBirth", "type": "date", "required": true},
                    {"key": "address", "type": "string", "required": true},
                    {"key": "householdSize", "type": "integer", "required": true},
                    {"key": "monthlyIncome", "type": "decimal", "required": true},
                    {"key": "requestedServices", "type": "array", "required": true},
                    {"key": "notes", "type": "string", "required": false}
                ]}'::JSONB),
            ('housing-stability', 'tenant-b', 'Housing Stability Intake',
                '{"fields": [
                    {"key": "applicantName", "type": "string", "required": true},
                    {"key": "dateOfBirth", "type": "date", "required": true},
                    {"key": "currentAddress", "type": "string", "required": true},
                    {"key": "householdMembers", "type": "integer", "required": true},
                    {"key": "monthlyRent", "type": "decimal", "required": true},
                    {"key": "evictionRiskLevel", "type": "string", "required": false},
                    {"key": "requestedAssistance", "type": "string", "required": true}
                ]}'::JSONB),
            ('behavioral-health', 'tenant-b', 'Behavioral Health Intake',
                '{"fields": [
                    {"key": "clientName", "type": "string", "required": true},
                    {"key": "dateOfBirth", "type": "date", "required": true},
                    {"key": "presentingConcern", "type": "string", "required": true},
                    {"key": "currentMedications", "type": "string", "required": false},
                    {"key": "substanceUse", "type": "string", "required": false},
                    {"key": "traumaHistory", "type": "string", "required": false},
                    {"key": "suicidalIdeation", "type": "string", "required": false}
                ]}'::JSONB),
            ('soap-note', 'tenant-b', 'SOAP Progress Note',
                '{"fields": [
                    {"key": "clientName", "type": "string", "required": true},
                    {"key": "sessionNumber", "type": "string", "required": true},
                    {"key": "subjective", "type": "string", "required": true},
                    {"key": "objective", "type": "string", "required": false},
                    {"key": "assessment", "type": "string", "required": false},
                    {"key": "plan", "type": "string", "required": false},
                    {"key": "riskLevel", "type": "string", "required": true}
                ]}'::JSONB)
        ON CONFLICT DO NOTHING;
        """;

    private const string SeedDocumentsSql = """
        INSERT INTO documents (id, tenant_id, template_id, uploaded_by, original_file_key, original_file_name, status)
        VALUES
            ('11111111-1111-1111-1111-111111111111', 'tenant-a', 'general-assistance',
                (SELECT id FROM users WHERE tenant_id = 'tenant-a' AND role = 'IntakeWorker' LIMIT 1),
                'tenant-a/seed/tenant-a-case-1.pdf', 'tenant-a-case-1.pdf', 'finalized'),
            ('22222222-2222-2222-2222-222222222222', 'tenant-a', 'general-assistance',
                (SELECT id FROM users WHERE tenant_id = 'tenant-a' AND role = 'IntakeWorker' LIMIT 1),
                'tenant-a/seed/tenant-a-case-2.pdf', 'tenant-a-case-2.pdf', 'finalized'),
            ('33333333-3333-3333-3333-333333333333', 'tenant-a', 'general-assistance',
                (SELECT id FROM users WHERE tenant_id = 'tenant-a' AND role = 'IntakeWorker' LIMIT 1),
                'tenant-a/seed/tenant-a-case-3.pdf', 'tenant-a-case-3.pdf', 'finalized'),
            ('44444444-4444-4444-4444-444444444444', 'tenant-b', 'general-assistance',
                (SELECT id FROM users WHERE tenant_id = 'tenant-b' AND role = 'IntakeWorker' LIMIT 1),
                'tenant-b/seed/tenant-b-case-1.pdf', 'tenant-b-case-1.pdf', 'finalized')
        ON CONFLICT (id) DO NOTHING;

        INSERT INTO extracted_fields (document_id, tenant_id, field_key, extracted_value, confidence, requires_review)
        VALUES
            ('11111111-1111-1111-1111-111111111111', 'tenant-a', 'applicantName', 'Jamie Carter', 0.97, false),
            ('11111111-1111-1111-1111-111111111111', 'tenant-a', 'dateOfBirth', '03/15/1988', 0.99, false),
            ('11111111-1111-1111-1111-111111111111', 'tenant-a', 'address', '742 Evergreen Terrace, Springfield', 0.98, false),
            ('11111111-1111-1111-1111-111111111111', 'tenant-a', 'householdSize', '4', 0.95, false),
            ('11111111-1111-1111-1111-111111111111', 'tenant-a', 'monthlyIncome', '$1,850', 0.94, false),
            ('11111111-1111-1111-1111-111111111111', 'tenant-a', 'requestedServices', 'Housing assistance, utility aid', 0.95, false),
            ('22222222-2222-2222-2222-222222222222', 'tenant-a', 'applicantName', 'Jamie Carrr', 0.95, false),
            ('22222222-2222-2222-2222-222222222222', 'tenant-a', 'dateOfBirth', '03/15/1988', 0.96, false),
            ('22222222-2222-2222-2222-222222222222', 'tenant-a', 'address', '742 Evergreen Ave, Springfield', 0.95, false),
            ('22222222-2222-2222-2222-222222222222', 'tenant-a', 'householdSize', '3', 0.93, false),
            ('22222222-2222-2222-2222-222222222222', 'tenant-a', 'monthlyIncome', '$1,900', 0.94, false),
            ('22222222-2222-2222-2222-222222222222', 'tenant-a', 'requestedServices', 'Emergency rent aid, utility aid', 0.95, false),
            ('33333333-3333-3333-3333-333333333333', 'tenant-a', 'applicantName', 'Luis Romero', 0.95, false),
            ('33333333-3333-3333-3333-333333333333', 'tenant-a', 'dateOfBirth', '11/05/1985', 0.93, false),
            ('33333333-3333-3333-3333-333333333333', 'tenant-a', 'address', '18 River Rd, Springfield', 0.95, false),
            ('33333333-3333-3333-3333-333333333333', 'tenant-a', 'householdSize', '2', 0.94, false),
            ('33333333-3333-3333-3333-333333333333', 'tenant-a', 'monthlyIncome', '$2,500', 0.94, false),
            ('33333333-3333-3333-3333-333333333333', 'tenant-a', 'requestedServices', 'Food assistance', 0.94, false),
            ('44444444-4444-4444-4444-444444444444', 'tenant-b', 'applicantName', 'Morgan Lee', 0.95, false),
            ('44444444-4444-4444-4444-444444444444', 'tenant-b', 'dateOfBirth', '07/08/1990', 0.95, false),
            ('44444444-4444-4444-4444-444444444444', 'tenant-b', 'address', '12 Lake Ave, Harbor City', 0.95, false),
            ('44444444-4444-4444-4444-444444444444', 'tenant-b', 'householdSize', '1', 0.93, false),
            ('44444444-4444-4444-4444-444444444444', 'tenant-b', 'monthlyIncome', '$3,100', 0.92, false),
            ('44444444-4444-4444-4444-444444444444', 'tenant-b', 'requestedServices', 'Job placement support', 0.94, false)
        ON CONFLICT (document_id, field_key) DO NOTHING;

        INSERT INTO case_profiles (document_id, tenant_id, template_id, applicant_name, date_of_birth, address, search_text)
        VALUES
            ('11111111-1111-1111-1111-111111111111', 'tenant-a', 'general-assistance',
                'Jamie Carter', '03/15/1988', '742 Evergreen Terrace, Springfield',
                'template=general-assistance; fields=applicantName: Jamie Carter | dateOfBirth: 03/15/1988 | address: 742 Evergreen Terrace, Springfield | householdSize: 4 | monthlyIncome: $1,850 | requestedServices: Housing assistance, utility aid'),
            ('22222222-2222-2222-2222-222222222222', 'tenant-a', 'general-assistance',
                'Jamie Carrr', '03/15/1988', '742 Evergreen Ave, Springfield',
                'template=general-assistance; fields=applicantName: Jamie Carrr | dateOfBirth: 03/15/1988 | address: 742 Evergreen Ave, Springfield | householdSize: 3 | monthlyIncome: $1,900 | requestedServices: Emergency rent aid, utility aid'),
            ('33333333-3333-3333-3333-333333333333', 'tenant-a', 'general-assistance',
                'Luis Romero', '11/05/1985', '18 River Rd, Springfield',
                'template=general-assistance; fields=applicantName: Luis Romero | dateOfBirth: 11/05/1985 | address: 18 River Rd, Springfield | householdSize: 2 | monthlyIncome: $2,500 | requestedServices: Food assistance'),
            ('44444444-4444-4444-4444-444444444444', 'tenant-b', 'general-assistance',
                'Morgan Lee', '07/08/1990', '12 Lake Ave, Harbor City',
                'template=general-assistance; fields=applicantName: Morgan Lee | dateOfBirth: 07/08/1990 | address: 12 Lake Ave, Harbor City | householdSize: 1 | monthlyIncome: $3,100 | requestedServices: Job placement support')
        ON CONFLICT (document_id) DO NOTHING;
        """;
}
