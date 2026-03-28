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

            // Phase 3c: Data cleanup (remove stale mock data from pre-existing DBs)
            await ExecuteSqlSafe(conn, CleanupMockDataSql, logger, "cleanup mock data");

            // Phase 4: Seed data (ON CONFLICT — idempotent)
            await ExecuteSqlSafe(conn, SeedTenantsSql, logger, "seed tenants");
            await ExecuteSqlSafe(conn, SeedUsersSql, logger, "seed users");
            await ExecuteSqlSafe(conn, CleanupOldTemplatesSql, logger, "cleanup old templates");
            await ExecuteSqlSafe(conn, SeedTemplatesSql, logger, "seed templates");

            // Phase 5: Corpus seed (only if case_profiles is nearly empty)
            await SeedCorpusIfNeededAsync(conn, logger);

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

    // Delete fabricated mock data left by earlier MockTesseractProvider seeds.
    // These rows have hardcoded UUIDs and technique='tesseract-mock' that no longer exist in the codebase.
    private const string CleanupMockDataSql = """
        -- Remove extraction attempts from mock provider
        DELETE FROM extraction_attempts WHERE provider = 'tesseract-mock';
        DELETE FROM extraction_attempts WHERE provider = 'mock-template-values';
        
        -- Remove documents with known mock UUIDs (Jamie Carter, Luis Romero, Morgan Lee, etc.)
        DELETE FROM audit_events WHERE document_id IN (
            '11111111-1111-1111-1111-111111111111',
            '22222222-2222-2222-2222-222222222222',
            '33333333-3333-3333-3333-333333333333',
            '44444444-4444-4444-4444-444444444444'
        );
        DELETE FROM extracted_fields WHERE document_id IN (
            '11111111-1111-1111-1111-111111111111',
            '22222222-2222-2222-2222-222222222222',
            '33333333-3333-3333-3333-333333333333',
            '44444444-4444-4444-4444-444444444444'
        );
        DELETE FROM case_profiles WHERE document_id IN (
            '11111111-1111-1111-1111-111111111111',
            '22222222-2222-2222-2222-222222222222',
            '33333333-3333-3333-3333-333333333333',
            '44444444-4444-4444-4444-444444444444'
        );
        DELETE FROM documents WHERE id IN (
            '11111111-1111-1111-1111-111111111111',
            '22222222-2222-2222-2222-222222222222',
            '33333333-3333-3333-3333-333333333333',
            '44444444-4444-4444-4444-444444444444'
        );
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

    /// <summary>
    /// Seeds a condensed corpus for the RAG demo if case_profiles has fewer than 10 rows.
    /// Includes key narrative arc people: P019 (Raymond Castillo), P039 (Gloria Navarro),
    /// P017 (Carlton Hughes), P037 (Bernard Oduya).
    /// </summary>
    private static async Task SeedCorpusIfNeededAsync(NpgsqlConnection conn, ILogger logger)
    {
        try
        {
            await using var countCmd = new NpgsqlCommand("SELECT COUNT(*)::int FROM case_profiles", conn);
            countCmd.CommandTimeout = 10;
            var count = (int)(await countCmd.ExecuteScalarAsync())!;
            if (count >= 10)
            {
                logger.LogInformation("DatabaseInitializer: corpus seed skipped ({Count} case_profiles exist)", count);
                return;
            }
            await ExecuteSqlSafe(conn, CorpusSeedDocsSql, logger, "corpus seed documents");
            await ExecuteSqlSafe(conn, CorpusSeedFieldsSql, logger, "corpus seed extracted_fields");
            await ExecuteSqlSafe(conn, CorpusSeedProfilesSql, logger, "corpus seed case_profiles");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DatabaseInitializer: corpus seed check failed, skipping");
        }
    }

    // Delete old template IDs that were renamed (financial-assistance -> behavioral-health, clinical-soap-note -> soap-note)
    private const string CleanupOldTemplatesSql = """
        DELETE FROM templates WHERE id IN ('financial-assistance', 'clinical-soap-note');
        """;

    // ---- Condensed corpus seed for RAG demo (P017, P019, P037, P039 — 8 documents) ----

    private const string CorpusSeedDocsSql = """
        INSERT INTO documents (id, tenant_id, template_id, uploaded_by, original_file_key, original_file_name, status) VALUES
            ('d2c4f896-9a6e-59da-8c94-f4902fab0dd6', 'tenant-a', 'general-assistance', (SELECT id FROM users WHERE tenant_id = 'tenant-a' AND role = 'IntakeWorker' LIMIT 1), 'tenant-a/seed/P017_01_v2_general-assistance.pdf', 'P017_01_v2_general-assistance.pdf', 'finalized'),
            ('e7c29e89-dd29-54e5-a731-8fcfabdc7cd8', 'tenant-b', 'general-assistance', (SELECT id FROM users WHERE tenant_id = 'tenant-b' AND role = 'IntakeWorker' LIMIT 1), 'tenant-b/seed/P017_03_v1_general-assistance.pdf', 'P017_03_v1_general-assistance.pdf', 'finalized'),
            ('308af6ee-4d4e-52c9-bb26-378bb9ec0dcb', 'tenant-a', 'general-assistance', (SELECT id FROM users WHERE tenant_id = 'tenant-a' AND role = 'IntakeWorker' LIMIT 1), 'tenant-a/seed/P019_01_v2_general-assistance.pdf', 'P019_01_v2_general-assistance.pdf', 'finalized'),
            ('2cac3ba1-be00-5770-a367-a7aba56c1f86', 'tenant-a', 'general-assistance', (SELECT id FROM users WHERE tenant_id = 'tenant-a' AND role = 'IntakeWorker' LIMIT 1), 'tenant-a/seed/P019_04_v1_general-assistance.pdf', 'P019_04_v1_general-assistance.pdf', 'finalized'),
            ('ad3e6add-f76e-55cd-b6dd-f4cb8d310115', 'tenant-b', 'general-assistance', (SELECT id FROM users WHERE tenant_id = 'tenant-b' AND role = 'IntakeWorker' LIMIT 1), 'tenant-b/seed/P037_01_v2_general-assistance.pdf', 'P037_01_v2_general-assistance.pdf', 'finalized'),
            ('efcecd28-a9f8-553e-9878-edc04345dabc', 'tenant-a', 'general-assistance', (SELECT id FROM users WHERE tenant_id = 'tenant-a' AND role = 'IntakeWorker' LIMIT 1), 'tenant-a/seed/P037_03_v1_general-assistance.pdf', 'P037_03_v1_general-assistance.pdf', 'finalized'),
            ('123a82ee-4255-576d-867f-eab443d12799', 'tenant-b', 'general-assistance', (SELECT id FROM users WHERE tenant_id = 'tenant-b' AND role = 'IntakeWorker' LIMIT 1), 'tenant-b/seed/P039_01_v2_general-assistance.pdf', 'P039_01_v2_general-assistance.pdf', 'finalized'),
            ('dd1c07bd-3a13-5774-94bf-5909f8d8026a', 'tenant-b', 'general-assistance', (SELECT id FROM users WHERE tenant_id = 'tenant-b' AND role = 'IntakeWorker' LIMIT 1), 'tenant-b/seed/P039_04_v1_general-assistance.pdf', 'P039_04_v1_general-assistance.pdf', 'finalized')
        ON CONFLICT (id) DO NOTHING;
        """;

    private const string CorpusSeedFieldsSql = """
        INSERT INTO extracted_fields (id, document_id, tenant_id, field_key, extracted_value, corrected_value, confidence, requires_review) VALUES
            -- P017 Carlton Hughes v2 (tenant-a)
            ('66c4fb42-b979-51a1-ab31-b01c73c23977', 'd2c4f896-9a6e-59da-8c94-f4902fab0dd6', 'tenant-a', 'applicantName', 'Carlton Hughes', NULL, 0.8814, false),
            ('5e4b8599-320f-5e8b-bb52-841b0adc1c00', 'd2c4f896-9a6e-59da-8c94-f4902fab0dd6', 'tenant-a', 'dateOfBirth', '09/08/1969', NULL, 0.7841, false),
            ('232c41f7-c1ad-5c04-85e1-d0f3ff3e2b35', 'd2c4f896-9a6e-59da-8c94-f4902fab0dd6', 'tenant-a', 'address', '534 Chestnut Blvd, Springfield, IL 62701', NULL, 0.6418, true),
            ('b490581a-08d9-5e6a-ac82-18c70f14bf2b', 'd2c4f896-9a6e-59da-8c94-f4902fab0dd6', 'tenant-a', 'householdSize', '2', NULL, 0.6231, true),
            ('b2a510f3-493d-5bf0-89d4-e4eb901ea33f', 'd2c4f896-9a6e-59da-8c94-f4902fab0dd6', 'tenant-a', 'monthlyIncome', '$0', NULL, 0.5500, true),
            ('f17a38c4-f4b0-5c39-b769-173d82ecce93', 'd2c4f896-9a6e-59da-8c94-f4902fab0dd6', 'tenant-a', 'reasonForAssistance', 'Laid off 2 months ago. Behind 3 months on rent. Received pay-or-quit notice.', NULL, 0.6798, true),
            -- P017 Carlton Hughes v1 (tenant-b, cross-facility transfer)
            ('f4f0eb2b-e9b8-5794-b88a-abeb223074b4', 'e7c29e89-dd29-54e5-a731-8fcfabdc7cd8', 'tenant-b', 'applicantName', 'Carlton Hughes', NULL, 0.9685, false),
            ('a43cc222-1aef-5d71-8838-d82ee7f222eb', 'e7c29e89-dd29-54e5-a731-8fcfabdc7cd8', 'tenant-b', 'dateOfBirth', '09/08/1969', NULL, 0.9561, false),
            ('c0500e73-f70b-5de1-840c-a0d273a113d1', 'e7c29e89-dd29-54e5-a731-8fcfabdc7cd8', 'tenant-b', 'address', '534 Chestnut Blvd, Springfield, IL 62701', NULL, 0.8469, false),
            ('506232a2-2b32-5e0d-a503-1eaa5144b21b', 'e7c29e89-dd29-54e5-a731-8fcfabdc7cd8', 'tenant-b', 'householdSize', '2', NULL, 0.8156, false),
            ('10fb879b-4494-5b6f-8b6d-ebf50ef603da', 'e7c29e89-dd29-54e5-a731-8fcfabdc7cd8', 'tenant-b', 'monthlyIncome', '$2200', NULL, 0.8798, false),
            ('248c9f23-65fe-54d2-8d33-9d6f7038ecc7', 'e7c29e89-dd29-54e5-a731-8fcfabdc7cd8', 'tenant-b', 'reasonForAssistance', 'Relocated for new employment. Need one-time assistance with moving deposit.', NULL, 0.5785, true),
            -- P019 Raymond Castillo v2 (tenant-a)
            ('5d7d107c-8e5b-563a-8bd8-21d762cb8e8c', '308af6ee-4d4e-52c9-bb26-378bb9ec0dcb', 'tenant-a', 'applicantName', 'Raymond Castillo', NULL, 0.9123, false),
            ('ca349c05-e4b4-5e79-a8ae-ce0210f320eb', '308af6ee-4d4e-52c9-bb26-378bb9ec0dcb', 'tenant-a', 'dateOfBirth', '06/05/1972', NULL, 0.8634, false),
            ('116ed982-808c-5b31-8a9e-602407a40e58', '308af6ee-4d4e-52c9-bb26-378bb9ec0dcb', 'tenant-a', 'address', '902 Ironwood Dr, Springfield, IL 62703', NULL, 0.7498, true),
            ('156398d5-3e09-50e1-9682-99a52c78e3ff', '308af6ee-4d4e-52c9-bb26-378bb9ec0dcb', 'tenant-a', 'householdSize', '1', NULL, 0.7020, true),
            ('3514651b-7e54-568f-80ee-5573d19fee8f', '308af6ee-4d4e-52c9-bb26-378bb9ec0dcb', 'tenant-a', 'monthlyIncome', '$0', NULL, 0.5500, true),
            ('95e9d05f-bff7-57ef-91af-a9817a254f56', '308af6ee-4d4e-52c9-bb26-378bb9ec0dcb', 'tenant-a', 'reasonForAssistance', 'Lost job 4 months ago. Currently sleeping at shelter. Rent arrears from prior unit still owed.', NULL, 0.5680, true),
            -- P019 Raymond Castillo v1 (tenant-a)
            ('a64db699-d1f9-50bd-b215-250b3e5265a7', '2cac3ba1-be00-5770-a367-a7aba56c1f86', 'tenant-a', 'applicantName', 'Raymond Castillo', NULL, 0.9040, false),
            ('3439ba43-0264-5d30-8b04-5de90a0879fc', '2cac3ba1-be00-5770-a367-a7aba56c1f86', 'tenant-a', 'dateOfBirth', '06/05/1972', NULL, 0.9612, false),
            ('67dcd41c-2547-5578-b021-5739b8c6af87', '2cac3ba1-be00-5770-a367-a7aba56c1f86', 'tenant-a', 'address', '902 Ironwood Dr, Springfield, IL 62703', NULL, 0.8597, false),
            ('bae6ea42-7e5f-5ce5-8e92-1257bacf7f55', '2cac3ba1-be00-5770-a367-a7aba56c1f86', 'tenant-a', 'householdSize', '1', NULL, 0.8905, false),
            ('b704ad01-ecc2-5292-a9f6-7fdf5bcfc5aa', '2cac3ba1-be00-5770-a367-a7aba56c1f86', 'tenant-a', 'monthlyIncome', '$650', NULL, 0.7879, false),
            ('4e623312-24ff-5e32-9250-ec8860a8a7de', '2cac3ba1-be00-5770-a367-a7aba56c1f86', 'tenant-a', 'reasonForAssistance', 'Secured part-time work at warehouse. Need help with first/last month rent deposit.', NULL, 0.5945, true),
            -- P037 Bernard Oduya v2 (tenant-b)
            ('259f4cc3-1d2a-56ca-ba46-8e98b04a6f01', 'ad3e6add-f76e-55cd-b6dd-f4cb8d310115', 'tenant-b', 'applicantName', 'Bernard Oduya', NULL, 0.8804, false),
            ('8a91ac92-aa99-5e69-9e1a-f8b60d66295a', 'ad3e6add-f76e-55cd-b6dd-f4cb8d310115', 'tenant-b', 'dateOfBirth', '07/14/1960', NULL, 0.8460, false),
            ('b7a19a90-2651-5f5d-b98b-231426127997', 'ad3e6add-f76e-55cd-b6dd-f4cb8d310115', 'tenant-b', 'address', '290 Overlook Ave, Lakewood, IL 60014', NULL, 0.7560, false),
            ('bd702fc6-9ea0-5afd-a94a-3532d31fa863', 'ad3e6add-f76e-55cd-b6dd-f4cb8d310115', 'tenant-b', 'householdSize', '1', NULL, 0.7020, true),
            ('852110b9-5b80-56db-a0e7-8e689fa204b3', 'ad3e6add-f76e-55cd-b6dd-f4cb8d310115', 'tenant-b', 'monthlyIncome', '$1100', NULL, 0.7658, false),
            ('95b94cd4-a0be-52e0-b03a-1ef1a83b904c', 'ad3e6add-f76e-55cd-b6dd-f4cb8d310115', 'tenant-b', 'reasonForAssistance', 'Landlord selling property, given 60-day notice. Fixed income. Limited options in current area.', NULL, 0.5626, true),
            -- P037 Bernard Oduya v1 (tenant-a, cross-facility transfer)
            ('7c254de8-fb9c-52b8-a8fc-52cf6c8c30a6', 'efcecd28-a9f8-553e-9878-edc04345dabc', 'tenant-a', 'applicantName', 'Bernard Oduya', NULL, 0.9118, false),
            ('ea8673ee-67be-5790-b7c0-feefcf1d37ed', 'efcecd28-a9f8-553e-9878-edc04345dabc', 'tenant-a', 'dateOfBirth', '07/14/1960', NULL, 0.9282, false),
            ('97bea34d-839a-5bc9-8a58-d99f81bd41f0', 'efcecd28-a9f8-553e-9878-edc04345dabc', 'tenant-a', 'address', '290 Overlook Ave, Lakewood, IL 60014', NULL, 0.7805, false),
            ('7f596b35-2d0f-5487-87d1-e9d0e4418230', 'efcecd28-a9f8-553e-9878-edc04345dabc', 'tenant-a', 'householdSize', '3', NULL, 0.8528, false),
            ('53b2b2bf-2086-55f8-931d-9cd8fcc8e9fa', 'efcecd28-a9f8-553e-9878-edc04345dabc', 'tenant-a', 'monthlyIncome', '$1100', NULL, 0.7136, true),
            ('2d7870f4-8337-552c-8f5d-d980b191deaa', 'efcecd28-a9f8-553e-9878-edc04345dabc', 'tenant-a', 'reasonForAssistance', 'Moved to Springfield to live with son. Household adjusting. Need help with medication costs.', NULL, 0.6150, true),
            -- P039 Gloria Navarro v2 (tenant-b)
            ('846a9ae4-3085-5cb0-b0e9-c4ff44a9d995', '123a82ee-4255-576d-867f-eab443d12799', 'tenant-b', 'applicantName', 'Gloria Navarro', NULL, 0.7965, false),
            ('0167f65d-645a-5a68-a66b-12fc69960ca8', '123a82ee-4255-576d-867f-eab443d12799', 'tenant-b', 'dateOfBirth', '11/12/1968', NULL, 0.8855, false),
            ('d0ad3022-efdd-5ec1-9177-0ded8f3fdd19', '123a82ee-4255-576d-867f-eab443d12799', 'tenant-b', 'address', '815 Clearwater Dr, Lakewood, IL 60014', NULL, 0.6355, true),
            ('1a81b408-bc8a-51f4-a401-e50fab61ce98', '123a82ee-4255-576d-867f-eab443d12799', 'tenant-b', 'householdSize', '5', NULL, 0.6157, true),
            ('7790f99c-0bad-5b1f-b4e8-5e142a83f690', '123a82ee-4255-576d-867f-eab443d12799', 'tenant-b', 'monthlyIncome', '$0', NULL, 0.5500, true),
            ('60a9c5c5-4171-5f1c-86d0-5606890c5e74', '123a82ee-4255-576d-867f-eab443d12799', 'tenant-b', 'reasonForAssistance', 'Staying with sister — overcrowded household of 5. No income. Need help paying for childcare to work.', NULL, 0.6672, true),
            -- P039 Gloria Navarro v1 (tenant-b)
            ('11170d79-288a-5ce9-95f1-369497d07397', 'dd1c07bd-3a13-5774-94bf-5909f8d8026a', 'tenant-b', 'applicantName', 'Gloria Navarro', NULL, 0.9197, false),
            ('c79bdbc2-4cae-5612-a53f-cc1cdd9c0237', 'dd1c07bd-3a13-5774-94bf-5909f8d8026a', 'tenant-b', 'dateOfBirth', '11/12/1968', NULL, 0.8924, false),
            ('b1f0bb96-bc7d-5b88-9bc5-29f942d56c96', 'dd1c07bd-3a13-5774-94bf-5909f8d8026a', 'tenant-b', 'address', '815 Clearwater Dr, Lakewood, IL 60014', NULL, 0.8079, false),
            ('453c2ac6-b914-595c-80ae-053833e437d0', 'dd1c07bd-3a13-5774-94bf-5909f8d8026a', 'tenant-b', 'householdSize', '2', NULL, 0.8156, false),
            ('13f0e8b1-2e43-5c00-8d57-a4cfd35320c0', 'dd1c07bd-3a13-5774-94bf-5909f8d8026a', 'tenant-b', 'monthlyIncome', '$780', NULL, 0.8215, false),
            ('6443d8a7-9b5c-5f4b-bd54-3d794edcfdbc', 'dd1c07bd-3a13-5774-94bf-5909f8d8026a', 'tenant-b', 'reasonForAssistance', 'Moved to own apartment via Section 8 voucher. Part-time cleaning work. Need help with utility setup.', NULL, 0.6125, true)
        ON CONFLICT (document_id, field_key) DO NOTHING;
        """;

    private const string CorpusSeedProfilesSql = """
        INSERT INTO case_profiles (document_id, tenant_id, template_id, applicant_name, date_of_birth, address, search_text, embedding) VALUES
            ('d2c4f896-9a6e-59da-8c94-f4902fab0dd6', 'tenant-a', 'general-assistance', 'Carlton Hughes', '09/08/1969', '534 Chestnut Blvd', 'Carlton Hughes DOB:09/08/1969 housing:Renting income:$0 household:2 Laid off 2 months ago. Behind 3 months on rent. Received pay-or-quit notice.', NULL),
            ('e7c29e89-dd29-54e5-a731-8fcfabdc7cd8', 'tenant-b', 'general-assistance', 'Carlton Hughes', '09/08/1969', '534 Chestnut Blvd', 'Carlton Hughes DOB:09/08/1969 housing:Renting income:$2200 household:2 Relocated for new employment. Need one-time assistance with moving deposit.', NULL),
            ('308af6ee-4d4e-52c9-bb26-378bb9ec0dcb', 'tenant-a', 'general-assistance', 'Raymond Castillo', '06/05/1972', '902 Ironwood Dr', 'Raymond Castillo DOB:06/05/1972 housing:Homeless income:$0 household:1 Lost job 4 months ago. Currently sleeping at shelter. Rent arrears from prior unit still owed.', NULL),
            ('2cac3ba1-be00-5770-a367-a7aba56c1f86', 'tenant-a', 'general-assistance', 'Raymond Castillo', '06/05/1972', '902 Ironwood Dr', 'Raymond Castillo DOB:06/05/1972 housing:Room rental income:$650 household:1 Secured part-time work at warehouse. Need help with first/last month rent deposit.', NULL),
            ('ad3e6add-f76e-55cd-b6dd-f4cb8d310115', 'tenant-b', 'general-assistance', 'Bernard Oduya', '07/14/1960', '290 Overlook Ave', 'Bernard Oduya DOB:07/14/1960 housing:Renting income:$1100 household:1 Landlord selling property, given 60-day notice. Fixed income. Limited options in current area.', NULL),
            ('efcecd28-a9f8-553e-9878-edc04345dabc', 'tenant-a', 'general-assistance', 'Bernard Oduya', '07/14/1960', '290 Overlook Ave', 'Bernard Oduya DOB:07/14/1960 housing:With family income:$1100 household:3 Moved to Springfield to live with son. Household adjusting. Need help with medication costs.', NULL),
            ('123a82ee-4255-576d-867f-eab443d12799', 'tenant-b', 'general-assistance', 'Gloria Navarro', '11/12/1968', '815 Clearwater Dr', 'Gloria Navarro DOB:11/12/1968 housing:With family income:$0 household:5 Staying with sister — overcrowded household of 5. No income. Need help paying for childcare to work.', NULL),
            ('dd1c07bd-3a13-5774-94bf-5909f8d8026a', 'tenant-b', 'general-assistance', 'Gloria Navarro', '11/12/1968', '815 Clearwater Dr', 'Gloria Navarro DOB:11/12/1968 housing:Renting income:$780 household:2 Moved to own apartment via Section 8 voucher. Part-time cleaning work. Need help with utility setup.', NULL)
        ON CONFLICT (document_id) DO NOTHING;
        """;
}