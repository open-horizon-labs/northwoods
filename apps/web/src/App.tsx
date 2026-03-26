import './App.css'

type CapabilityCardProps = {
  title: string
  description: string
  tag: string
}

function CapabilityCard({ title, description, tag }: CapabilityCardProps) {
  return (
    <article className="capability-card">
      <div>
        <p className="eyebrow">{tag}</p>
        <h2>{title}</h2>
      </div>
      <p>{description}</p>
    </article>
  )
}

const milestones = [
  'Authenticate as an intake worker or reviewer within a tenant boundary.',
  'Upload a handwritten intake packet for one representative template.',
  'Run asynchronous extraction and persist confidence-scored draft fields.',
  'Review uncertain fields, correct them, and finalize the record with an audit trail.',
]

export default function App() {
  return (
    <main className="shell">
      <section className="hero">
        <p className="eyebrow">Northwoods · review-ready intake loop</p>
        <h1>Walking skeleton for handwritten intake review</h1>
        <p className="lede">
          One API, one worker, one database. The first vertical slice proves the
          upload → extract → review → finalize trust loop end to end.
        </p>
      </section>

      <section className="grid" aria-label="Capability map">
        <CapabilityCard
          tag="auth"
          title="Tenant-aware authentication"
          description="Issue role-scoped development tokens carrying tenant context for intake workers and reviewers."
        />
        <CapabilityCard
          tag="intakes"
          title="Upload and extraction orchestration"
          description="Accept a template-guided upload, persist the intake, and start the extraction workflow."
        />
        <CapabilityCard
          tag="reviews"
          title="Confidence-aware review"
          description="Present extracted fields, highlight uncertainty, capture corrections, and finalize with audit history."
        />
      </section>

      <section className="panel">
        <div>
          <p className="eyebrow">Representative slice</p>
          <h2>Exit criteria</h2>
        </div>
        <ol>
          {milestones.map((milestone) => (
            <li key={milestone}>{milestone}</li>
          ))}
        </ol>
      </section>
    </main>
  )
}
