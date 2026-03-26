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
    password_hash TEXT NOT NULL DEFAULT 'dev',
    role TEXT NOT NULL CHECK (role IN ('IntakeWorker', 'Reviewer')),
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
    created_at TIMESTAMPTZ DEFAULT now(),
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
    status TEXT NOT NULL DEFAULT 'uploaded' CHECK (status IN ('uploaded', 'extracting', 'review_ready', 'finalized', 'failed')),
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
-- Create audit_events table
-- ============================================================================

CREATE TABLE IF NOT EXISTS audit_events (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id UUID NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
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
ALTER TABLE audit_events ENABLE ROW LEVEL SECURITY;

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
    ('tenant-a', 'worker@sunrise.example', 'dev', 'IntakeWorker'),
    ('tenant-a', 'reviewer@sunrise.example', 'dev', 'Reviewer')
ON CONFLICT (tenant_id, email) DO NOTHING;

-- ============================================================================
-- Seed data: Users for tenant-b
-- ============================================================================

INSERT INTO users (tenant_id, email, password_hash, role) VALUES
    ('tenant-b', 'worker@lakewood.example', 'dev', 'IntakeWorker'),
    ('tenant-b', 'reviewer@lakewood.example', 'dev', 'Reviewer')
ON CONFLICT (tenant_id, email) DO NOTHING;

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
    ('general-assistance', 'tenant-b', 'General Assistance Intake',
        '{"fields": [
            {"key": "applicantName", "type": "string", "required": true},
            {"key": "dateOfBirth", "type": "date", "required": true},
            {"key": "address", "type": "string", "required": true},
            {"key": "householdSize", "type": "integer", "required": true},
            {"key": "monthlyIncome", "type": "decimal", "required": true},
            {"key": "requestedServices", "type": "array", "required": true},
            {"key": "notes", "type": "string", "required": false}
        ]}'::JSONB)
ON CONFLICT DO NOTHING;
