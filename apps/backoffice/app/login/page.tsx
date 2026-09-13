import { SignIn } from '../../components/sign-in';

export default function LoginPage() {
  return <main className="login-page"><div className="login-brand"><span className="brand-word">Cover</span><span className="brand-subtitle">Motor Trade MGA</span></div>
    <section className="login-card" aria-labelledby="sign-in-title"><div className="login-intro"><p className="eyebrow">Back office</p><h1 id="sign-in-title">Welcome back</h1><p>Sign in to your Cover workspace.</p></div><SignIn />
      <p className="login-help">Need access? Contact your back office administrator.</p></section>
    <p className="demo-label">DEMO WORKSPACE · FICTIONAL DATA</p>
  </main>;
}
