// Tests de charge Souqly (k6) : les objectifs de service (SLO) sont écrits comme seuils.
// Si un seuil n'est pas tenu, k6 se termine avec le code 99 et la CI échoue.
//
//   k6 run tests/load/souqly.js                                  # fumée (1 utilisateur, 30 s)
//   k6 run -e PROFILE=charge tests/load/souqly.js                # charge nominale (≈ 60 req/s, 5 min)
//   k6 run -e PROFILE=pic -e BASE_URL=https://api.souqly.ma tests/load/souqly.js
//
// Voir tests/load/README.md (limite de débit de la passerelle, comptes, lecture des résultats).
import http from 'k6/http';
import { check, fail, group, sleep } from 'k6';
import { Counter, Trend } from 'k6/metrics';
import { randomItem, uuidv4 } from './lib.js';

const BASE_URL = (__ENV.BASE_URL || 'http://localhost:8080').replace(/\/$/, '');
const KEYCLOAK_URL = (__ENV.KEYCLOAK_URL || 'http://localhost:8180').replace(/\/$/, '');
const PROFILE = __ENV.PROFILE || 'fumee';
const USERNAME = __ENV.SOUQLY_USER || 'sara';
const PASSWORD = __ENV.SOUQLY_PASSWORD || 'Souqly-Dev-2026!';

const SEARCH_TERMS = ['tapis', 'tapi berbre', 'argane', 'ARGANE', 'poterie', 'babouches', 'ras el hanout', 'tajine', 'caftan'];

const ordersAccepted = new Counter('souqly_commandes_acceptees');
const orderLatency = new Trend('souqly_commande_ms', true);

// Profils : arrivée à débit constant (le débit ne baisse pas si le système ralentit, contrairement à un nombre fixe d'utilisateurs).
const PROFILES = {
  fumee: {
    navigation: { executor: 'constant-vus', vus: 1, duration: '30s', exec: 'navigation' },
    commande: { executor: 'constant-vus', vus: 1, duration: '30s', exec: 'commande' },
  },
  charge: {
    navigation: {
      executor: 'ramping-arrival-rate', exec: 'navigation', startRate: 5, timeUnit: '1s',
      preAllocatedVUs: 40, maxVUs: 150,
      stages: [{ target: 50, duration: '1m' }, { target: 50, duration: '3m' }, { target: 0, duration: '30s' }],
    },
    commande: {
      executor: 'constant-arrival-rate', exec: 'commande', rate: 2, timeUnit: '1s',
      duration: '4m30s', preAllocatedVUs: 10, maxVUs: 40,
    },
  },
  // Vente flash : le trafic du catalogue est multiplié par 4 en 20 s (c'est ce pic que le HPA doit absorber).
  pic: {
    navigation: {
      executor: 'ramping-arrival-rate', exec: 'navigation', startRate: 20, timeUnit: '1s',
      preAllocatedVUs: 80, maxVUs: 400,
      stages: [{ target: 50, duration: '30s' }, { target: 200, duration: '20s' }, { target: 200, duration: '2m' }, { target: 50, duration: '30s' }],
    },
    commande: {
      executor: 'constant-arrival-rate', exec: 'commande', rate: 5, timeUnit: '1s',
      duration: '3m20s', preAllocatedVUs: 20, maxVUs: 80,
    },
  },
};

if (!PROFILES[PROFILE]) {
  throw new Error(`PROFILE inconnu : ${PROFILE} (fumee, charge ou pic)`);
}

export const options = {
  scenarios: PROFILES[PROFILE],
  // SLO (docs/architecture/arc42.md §10) : un seuil franchi fait échouer le test.
  thresholds: {
    'http_req_duration{endpoint:catalogue}': ['p(95)<300'],
    'http_req_duration{endpoint:fiche}': ['p(95)<300'],
    'http_req_duration{endpoint:recherche}': ['p(95)<500'],
    'http_req_duration{endpoint:commande}': ['p(95)<800'],
    http_req_failed: [{ threshold: 'rate<0.01', abortOnFail: PROFILE !== 'fumee', delayAbortEval: '30s' }],
    checks: ['rate>0.99'],
    souqly_commandes_acceptees: ['count>0'],
  },
  summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

// Exécuté une fois : jeton du client de test et liste des produits en vente.
export function setup() {
  const token = http.post(
    `${KEYCLOAK_URL}/realms/souqly/protocol/openid-connect/token`,
    { grant_type: 'password', client_id: 'souqly-dev', username: USERNAME, password: PASSWORD },
    { tags: { endpoint: 'jeton' } },
  );
  if (token.status !== 200) {
    fail(`Connexion de ${USERNAME} impossible (${token.status}) : vérifier KEYCLOAK_URL et le compte.`);
  }

  const page = http.get(`${BASE_URL}/api/v1/products?size=50`, { tags: { endpoint: 'preparation' } });
  const products = page.status === 200 ? (page.json('content') || []) : [];
  if (products.length === 0) {
    fail(`Aucun produit en vente sur ${BASE_URL} (statut ${page.status}).`);
  }
  // Les produits bon marché évitent le refus de paiement (> 10 000 MAD), qui n'est pas une erreur technique.
  const cheap = products.filter((p) => Number(p.price?.amount ?? 0) < 2000).map((p) => p.id);
  return {
    token: token.json('access_token'),
    productIds: products.map((p) => p.id),
    orderableIds: cheap.length > 0 ? cheap : products.map((p) => p.id),
  };
}

// Visiteur : catalogue, recherche, fiche produit (≈ 95 % du trafic réel).
export function navigation(data) {
  group('catalogue', () => {
    const res = http.get(`${BASE_URL}/api/v1/products?page=0&size=20`, { tags: { endpoint: 'catalogue' } });
    check(res, { 'catalogue 200': (r) => r.status === 200 });
  });

  group('recherche', () => {
    const q = encodeURIComponent(randomItem(SEARCH_TERMS));
    const res = http.get(`${BASE_URL}/api/v1/search?q=${q}&size=20`, { tags: { endpoint: 'recherche' } });
    check(res, { 'recherche 200': (r) => r.status === 200 });
  });

  group('fiche', () => {
    const id = randomItem(data.productIds);
    const res = http.get(`${BASE_URL}/api/v1/products/${id}`, { tags: { endpoint: 'fiche' } });
    check(res, { 'fiche 200': (r) => r.status === 200 });
  });

  sleep(Math.random());
}

// Client connecté : passe une commande (réponse 202, la saga continue en arrière-plan).
export function commande(data) {
  const body = JSON.stringify({ lines: [{ productId: randomItem(data.orderableIds), quantity: 1 }] });
  const res = http.post(`${BASE_URL}/api/v1/orders`, body, {
    headers: {
      Authorization: `Bearer ${data.token}`,
      'Content-Type': 'application/json',
      'Idempotency-Key': uuidv4(),
    },
    tags: { endpoint: 'commande' },
  });
  const ok = check(res, {
    'commande 202': (r) => r.status === 202,
    'commande a un numéro': (r) => r.status !== 202 || !!r.json('id'),
  });
  if (ok) {
    ordersAccepted.add(1);
    orderLatency.add(res.timings.duration);
  }
}

export function handleSummary(data) {
  const out = __ENV.SUMMARY_FILE || `k6-${PROFILE}-resume.json`;
  return {
    stdout: textSummary(data),
    [out]: JSON.stringify(data, null, 2),
  };
}

// Résumé lisible en français (seuils OK/KO), sans dépendance externe.
function textSummary(data) {
  const lines = [`\nSouqly · profil « ${PROFILE} » · ${BASE_URL}\n`];
  for (const [name, metric] of Object.entries(data.metrics)) {
    if (!metric.thresholds) continue;
    for (const [rule, result] of Object.entries(metric.thresholds)) {
      const v = metric.values;
      const shown = v['p(95)'] !== undefined ? `p95 = ${v['p(95)'].toFixed(0)} ms`
        : v.rate !== undefined ? `taux = ${(v.rate * 100).toFixed(2)} %`
        : v.count !== undefined ? `nombre = ${v.count}` : '';
      lines.push(`  ${result.ok ? 'OK' : 'KO'}  ${name}  ${rule}  (${shown})`);
    }
  }
  return lines.join('\n') + '\n';
}
