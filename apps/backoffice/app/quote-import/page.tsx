import { QuoteDemoImport } from '../../components/quotes/quote-demo-import';

export default function QuoteImportPage() {
  return <main className="login-page"><div className="login-brand"><span className="brand-word">Cover</span><span className="brand-subtitle">Motor Trade MGA</span></div>
    <section className="login-card" aria-labelledby="quote-import-title"><div className="login-intro"><p className="eyebrow">Business review</p><h1 id="quote-import-title">Save demo quote</h1><p>Keep this window open while the funnel sends the completed answers.</p></div><QuoteDemoImport /></section>
    <p className="demo-label">DEMO WORKSPACE · FICTIONAL DATA</p>
  </main>;
}
