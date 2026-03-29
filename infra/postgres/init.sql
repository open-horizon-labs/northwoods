-- Enable extensions required by ADR 001
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- ============================================================================
-- Create tenant root table (no RLS needed)
-- ============================================================================

CREATE TABLE IF NOT EXISTS tenants (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    created_at TIMESTAMPTZ DEFAULT now()
);

-- ============================================================================
-- Create users table
-- ============================================================================

CREATE TABLE IF NOT EXISTS users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    email TEXT NOT NULL,
    password_hash TEXT NOT NULL,
    role TEXT NOT NULL CHECK (role IN ('IntakeWorker', 'Reviewer', 'Admin')),
    created_at TIMESTAMPTZ DEFAULT now(),
    UNIQUE(tenant_id, email)
);

-- ============================================================================
-- Create templates table
-- ============================================================================

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

-- ============================================================================
-- Create documents table
-- ============================================================================

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

-- ============================================================================
-- Create extracted_fields table
-- ============================================================================

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

-- ============================================================================
-- Create case profiles table for hybrid retrieval
-- ============================================================================

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

-- ==========================================================================
-- Create extraction attempts table for consensus/audit trail
-- ==========================================================================
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

CREATE INDEX IF NOT EXISTS idx_extraction_attempts_document_id ON extraction_attempts(document_id);
CREATE INDEX IF NOT EXISTS idx_extraction_attempts_tenant_id ON extraction_attempts(tenant_id);

-- ==========================================================================
-- Create audit_events table
-- ============================================================================

CREATE TABLE IF NOT EXISTS audit_events (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id UUID REFERENCES documents(id) ON DELETE CASCADE,
    tenant_id TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
    event_type TEXT NOT NULL,
    details JSONB,
    actor_id UUID REFERENCES users(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ DEFAULT now()
);

-- ============================================================================
-- Create indexes
-- ============================================================================

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
CREATE INDEX IF NOT EXISTS idx_case_profiles_embedding_hnsw ON case_profiles USING hnsw (embedding vector_cosine_ops);
CREATE INDEX IF NOT EXISTS idx_audit_events_tenant_id ON audit_events(tenant_id);
CREATE INDEX IF NOT EXISTS idx_audit_events_document_id ON audit_events(document_id);

-- ============================================================================
-- Create app_user role with password
-- ============================================================================

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_user') THEN
        CREATE ROLE app_user WITH LOGIN PASSWORD 'app_user';
    END IF;
END
$$;

-- ============================================================================
-- Grant permissions to app_user
-- ============================================================================

GRANT SELECT, INSERT, UPDATE, DELETE ON tenants TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON users TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON templates TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON documents TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON extracted_fields TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON case_profiles TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON extraction_attempts TO app_user;
GRANT SELECT, INSERT, UPDATE, DELETE ON audit_events TO app_user;

-- Grant sequence permissions for UUID generation
GRANT USAGE ON ALL SEQUENCES IN SCHEMA public TO app_user;

-- ============================================================================
-- Enable Row Level Security (RLS)
-- ============================================================================

ALTER TABLE users ENABLE ROW LEVEL SECURITY;
ALTER TABLE templates ENABLE ROW LEVEL SECURITY;
ALTER TABLE documents ENABLE ROW LEVEL SECURITY;
ALTER TABLE extracted_fields ENABLE ROW LEVEL SECURITY;
ALTER TABLE case_profiles ENABLE ROW LEVEL SECURITY;
ALTER TABLE audit_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE extraction_attempts ENABLE ROW LEVEL SECURITY;

-- ============================================================================
-- RLS Policies for users table
-- ============================================================================

CREATE POLICY users_tenant_isolation ON users
    USING (tenant_id = current_setting('app.tenant_id', true))
    WITH CHECK (tenant_id = current_setting('app.tenant_id', true));

-- ============================================================================
-- RLS Policies for templates table
-- ============================================================================

CREATE POLICY templates_tenant_isolation ON templates
    USING (tenant_id = current_setting('app.tenant_id', true))
    WITH CHECK (tenant_id = current_setting('app.tenant_id', true));

-- ============================================================================
-- RLS Policies for documents table
-- ============================================================================

CREATE POLICY documents_tenant_isolation ON documents
    USING (tenant_id = current_setting('app.tenant_id', true))
    WITH CHECK (tenant_id = current_setting('app.tenant_id', true));

-- ============================================================================
-- RLS Policies for extracted_fields table
-- ============================================================================

CREATE POLICY extracted_fields_tenant_isolation ON extracted_fields
    USING (tenant_id = current_setting('app.tenant_id', true))
    WITH CHECK (tenant_id = current_setting('app.tenant_id', true));

-- ============================================================================
-- RLS Policies for case_profiles table
-- ============================================================================

CREATE POLICY case_profiles_tenant_isolation ON case_profiles
    USING (tenant_id = current_setting('app.tenant_id', true))
    WITH CHECK (tenant_id = current_setting('app.tenant_id', true));

-- ============================================================================
-- RLS Policies for extraction_attempts table
-- ============================================================================

CREATE POLICY extraction_attempts_tenant_isolation ON extraction_attempts
    USING (tenant_id = current_setting('app.tenant_id', true))
    WITH CHECK (tenant_id = current_setting('app.tenant_id', true));

-- ============================================================================
-- RLS Policies for audit_events table
-- ============================================================================

CREATE POLICY audit_events_tenant_isolation ON audit_events
    USING (tenant_id = current_setting('app.tenant_id', true))
    WITH CHECK (tenant_id = current_setting('app.tenant_id', true));

-- ============================================================================
-- Seed data: Tenants
-- ============================================================================

INSERT INTO tenants (id, name) VALUES
    ('tenant-a', 'Sunrise County DHS'),
    ('tenant-b', 'Lakewood Family Services')
ON CONFLICT (id) DO NOTHING;

-- ============================================================================
-- Seed data: Users for tenant-a
-- ============================================================================

INSERT INTO users (tenant_id, email, password_hash, role) VALUES
    ('tenant-a', 'worker@sunrise.example', '$2a$12$S.9UQ5kYJy1e7DJ/f29XnOwGKrhVCo51W2rQ.NENXd.Zo.PHWoEai', 'IntakeWorker'),
    ('tenant-a', 'reviewer@sunrise.example', '$2a$12$S.9UQ5kYJy1e7DJ/f29XnOwGKrhVCo51W2rQ.NENXd.Zo.PHWoEai', 'Reviewer'),
    ('tenant-a', 'admin@sunrise.example', '$2a$12$S.9UQ5kYJy1e7DJ/f29XnOwGKrhVCo51W2rQ.NENXd.Zo.PHWoEai', 'Admin')
ON CONFLICT (tenant_id, email) DO UPDATE SET password_hash = EXCLUDED.password_hash;

-- ============================================================================
-- Seed data: Users for tenant-b
-- ============================================================================

INSERT INTO users (tenant_id, email, password_hash, role) VALUES
    ('tenant-b', 'worker@lakewood.example', '$2a$12$S.9UQ5kYJy1e7DJ/f29XnOwGKrhVCo51W2rQ.NENXd.Zo.PHWoEai', 'IntakeWorker'),
    ('tenant-b', 'reviewer@lakewood.example', '$2a$12$S.9UQ5kYJy1e7DJ/f29XnOwGKrhVCo51W2rQ.NENXd.Zo.PHWoEai', 'Reviewer'),
    ('tenant-b', 'admin@lakewood.example', '$2a$12$S.9UQ5kYJy1e7DJ/f29XnOwGKrhVCo51W2rQ.NENXd.Zo.PHWoEai', 'Admin')
ON CONFLICT (tenant_id, email) DO UPDATE SET password_hash = EXCLUDED.password_hash;

-- ============================================================================
-- Seed data: Templates
-- ============================================================================

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

-- Corpus seed: now handled by DatabaseInitializer.SeedCorpusAsync on API startup.
-- The old seed_corpus.sql (generated by scripts/corpus/generate_seed_sql.py) has been removed.