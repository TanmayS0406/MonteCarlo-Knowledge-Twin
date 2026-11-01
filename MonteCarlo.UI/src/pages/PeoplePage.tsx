import { useState } from 'react'
import { api, fmtDate, ROLES, type Person, type WorkRole } from '../api'
import { ErrorBox, Loading, useLoad } from '../components/common'

const blank = { name: '', email: '', externalUserId: '', role: 'Backend' as WorkRole, skills: '', capacity: 1, startDate: '', leaveDate: '' }

export default function PeoplePage() {
  const { data, error, reload } = useLoad(() => api.people())
  const [form, setForm] = useState(blank)
  const [editId, setEditId] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)

  function edit(p: Person) {
    setEditId(p.id)
    setForm({ name: p.name, email: p.email, externalUserId: p.externalUserId ?? '', role: p.role, skills: p.skills, capacity: p.capacity, startDate: p.startDate, leaveDate: p.leaveDate ?? '' })
  }

  async function save() {
    setErr(null)
    const body = { ...form, startDate: form.startDate || null, leaveDate: form.leaveDate || null, capacity: Number(form.capacity) }
    try {
      if (editId) await api.updatePerson(editId, body)
      else await api.createPerson(body)
      setForm(blank); setEditId(null); reload()
    } catch (e) { setErr((e as Error).message) }
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Team</h1>
          <p>Roles and capacity feed the simulator. Names are matched to transcript speakers. The product tracks work, not people's productivity.</p>
        </div>
      </div>
      <div className="grid" style={{ gridTemplateColumns: 'minmax(0,2fr) minmax(280px,1fr)' }}>
        <div className="card">
          <ErrorBox error={error} />
          {!data ? <Loading /> : (
            <table>
              <thead><tr><th>Name</th><th>Role</th><th>Capacity</th><th>Open</th><th>Dates</th><th></th></tr></thead>
              <tbody>
                {data.map((p) => (
                  <tr key={p.id}>
                    <td><b>{p.name}</b><div className="muted small">{p.email}{p.skills && <> · {p.skills}</>}</div></td>
                    <td><span className="badge">{p.role}</span></td>
                    <td>{Math.round(p.capacity * 100)}%</td>
                    <td>{p.openItems}</td>
                    <td className="small muted">from {fmtDate(p.startDate)}{p.leaveDate && <><br /><span style={{ color: 'var(--bad)' }}>leaves {fmtDate(p.leaveDate)}</span></>}</td>
                    <td className="row" style={{ gap: 4 }}>
                      <button className="btn sm" onClick={() => edit(p)}>Edit</button>
                      <button className="btn sm danger" onClick={async () => { if (confirm(`Remove ${p.name}?`)) { await api.deletePerson(p.id); reload() } }}>Remove</button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
        <div className="card stack" style={{ alignSelf: 'start' }}>
          <h2>{editId ? 'Edit person' : 'Add person'}</h2>
          <ErrorBox error={err} />
          <label className="field">Name<input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} /></label>
          <label className="field">Email<input value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} /></label>
          <label className="field">Teams user id (optional)<input value={form.externalUserId} onChange={(e) => setForm({ ...form, externalUserId: e.target.value })} /></label>
          <div className="grid g2">
            <label className="field">Role<select value={form.role} onChange={(e) => setForm({ ...form, role: e.target.value as WorkRole })}>{ROLES.map((r) => <option key={r}>{r}</option>)}</select></label>
            <label className="field">Capacity (0-1)<input type="number" min={0.05} max={1} step={0.05} value={form.capacity} onChange={(e) => setForm({ ...form, capacity: Number(e.target.value) })} /></label>
          </div>
          <label className="field">Skills<input value={form.skills} onChange={(e) => setForm({ ...form, skills: e.target.value })} /></label>
          <div className="grid g2">
            <label className="field">Start date<input type="date" value={form.startDate} onChange={(e) => setForm({ ...form, startDate: e.target.value })} /></label>
            <label className="field">Leave date<input type="date" value={form.leaveDate} onChange={(e) => setForm({ ...form, leaveDate: e.target.value })} /></label>
          </div>
          <div className="row">
            <button className="btn primary" disabled={!form.name} onClick={save}>{editId ? 'Save' : 'Add'}</button>
            {editId && <button className="btn" onClick={() => { setEditId(null); setForm(blank) }}>Cancel</button>}
          </div>
        </div>
      </div>
    </>
  )
}
