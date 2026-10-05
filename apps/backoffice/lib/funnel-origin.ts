// These destination pairs were explicitly approved for Motor Trade capture.
export function funnelOrigin() {
  return typeof window !== 'undefined' && window.location.origin === 'https://cx1-admin-dev.gyongyos.co.uk'
    ? 'https://cx1-dev.gyongyos.co.uk' : 'http://127.0.0.1:3177';
}
export function localFunnelParent(origin: string) {
  return ['http://127.0.0.1:3193', 'https://cx1-admin-dev.gyongyos.co.uk'].includes(origin);
}
