import { useEffect, useState } from 'react'
import { NavLink, Route, Routes, useLocation } from 'react-router-dom'
import { api } from './api'
import DashboardPage from './pages/DashboardPage'
import MeetingsPage from './pages/MeetingsPage'
import MeetingDetailPage from './pages/MeetingDetailPage'
import ReviewPage from './pages/ReviewPage'
import BoardPage from './pages/BoardPage'
import SearchPage from './pages/SearchPage'
import PeoplePage from './pages/PeoplePage'
import SimulatorPage from './pages/SimulatorPage'
import MonteCarloLabPage from './pages/MonteCarloLabPage'
import SettingsPage from './pages/SettingsPage'

export default function App() {
  const [pending, setPending] = useState(0)
  const [online, setOnline] = useState<boolean | null>(null)
  const location = useLocation()

  useEffect(() => {
    api.suggestions('Pending').then((s) => { setPending(s.length); setOnline(true) }).catch(() => setOnline(false))
  }, [location.pathname])

  const links: [string, string, number?][] = [
    ['/', 'Dashboard'],
    ['/meetings', 'Meetings'],
    ['/review', 'Review', pending],
    ['/board', 'Board'],
    ['/search', 'Why? search'],
    ['/simulator', 'What-if simulator'],
    ['/lab', '3D Monte Carlo'],
    ['/people', 'Team'],
    ['/settings', 'Privacy & settings'],
  ]

  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="brand">
          <img src="/favicon.svg" alt="" />
          <div><b>MonteCarlo</b><span>Team Knowledge Twin</span></div>
        </div>
        <nav className="nav stack" style={{ gap: 2 }}>
          {links.map(([to, label, count]) => (
            <NavLink key={to} to={to} end={to === '/'}>
              {label}
              {count ? <span className="count">{count}</span> : null}
            </NavLink>
          ))}
        </nav>
        <div className="sidebar-foot">
          {online === false
            ? <span style={{ color: 'var(--bad)' }}>API offline. Start the backend on :5080.</span>
            : 'Capture by default. Humans approve every change.'}
        </div>
      </aside>
      <main className="main">
        <Routes>
          <Route path="/" element={<DashboardPage />} />
          <Route path="/meetings" element={<MeetingsPage />} />
          <Route path="/meetings/:id" element={<MeetingDetailPage />} />
          <Route path="/review" element={<ReviewPage onChange={() => api.suggestions('Pending').then((s) => setPending(s.length))} />} />
          <Route path="/board" element={<BoardPage />} />
          <Route path="/search" element={<SearchPage />} />
          <Route path="/simulator" element={<SimulatorPage />} />
          <Route path="/lab" element={<MonteCarloLabPage />} />
          <Route path="/people" element={<PeoplePage />} />
          <Route path="/settings" element={<SettingsPage />} />
        </Routes>
      </main>
    </div>
  )
}
