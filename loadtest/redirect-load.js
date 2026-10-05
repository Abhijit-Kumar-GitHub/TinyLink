// Load test for the redirect path, used to drive the HorizontalPodAutoscaler.
//   k6 run loadtest/redirect-load.js
//   k6 run -e BASE_URL=http://<elastic-ip> -e CODE=demo loadtest/redirect-load.js
// Kept deliberately short: on t3.micro a long test drains CPU credits and the host throttles,
// which would measure AWS's burst limit rather than the autoscaler.
import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8088';
const CODE = __ENV.CODE || 'demo';

export const options = {
  stages: [
    { duration: '20s', target: 20 },  // warm up
    { duration: '90s', target: 60 },  // sustained load: HPA should scale out
    { duration: '20s', target: 0 },   // ramp down: HPA scales back in after its 60s window
  ],
  // Never follow the 302: we are testing TinyLink, not the destination site.
  maxRedirects: 0,
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<250'],
  },
};

export default function () {
  const res = http.get(`${BASE_URL}/${CODE}`, { tags: { name: 'redirect' } });
  check(res, {
    'is 302': (r) => r.status === 302,
    'has Location': (r) => !!r.headers['Location'],
  });
}
