const { createHash, X509Certificate } = require('crypto');

function normalizeHostname(value) {
  if (typeof value !== 'string' || value.trim() === '') {
    throw new Error('Certificate pin host names must be non-empty strings.');
  }

  return value.trim().toLowerCase();
}

function normalizeFingerprint256(value) {
  if (typeof value !== 'string' || value.trim() === '') {
    throw new Error('Certificate SHA-256 fingerprints must be non-empty strings.');
  }

  const normalized = value
    .trim()
    .toUpperCase()
    .replace(/[^A-F0-9]/g, '');

  if (normalized.length !== 64) {
    throw new Error(`Invalid SHA-256 fingerprint: ${value}`);
  }

  return normalized.match(/.{2}/g).join(':');
}

function normalizeSpkiSha256(value) {
  if (typeof value !== 'string' || value.trim() === '') {
    throw new Error('SPKI SHA-256 pins must be non-empty strings.');
  }

  return value.trim();
}

function toArray(value) {
  if (Array.isArray(value)) {
    return value;
  }

  if (value === undefined || value === null || value === '') {
    return [];
  }

  return [value];
}

function parseCertificatePinRule(rawRule) {
  if (!rawRule || typeof rawRule !== 'object') {
    throw new Error('Each certificate pin rule must be an object.');
  }

  const host = normalizeHostname(rawRule.host || rawRule.hostname);
  const includeSubdomains = rawRule.includeSubdomains === true;
  const fingerprint256 = toArray(rawRule.fingerprint256 || rawRule.fingerprints256 || rawRule.fingerprints)
    .map(normalizeFingerprint256);
  const spkiSha256 = toArray(rawRule.spkiSha256 || rawRule.spki || rawRule.pins)
    .map(normalizeSpkiSha256);

  if (fingerprint256.length === 0 && spkiSha256.length === 0) {
    throw new Error(`Certificate pin rule for ${host} must define fingerprint256 and/or spkiSha256 pins.`);
  }

  return {
    host,
    includeSubdomains,
    fingerprint256,
    spkiSha256,
  };
}

function loadCertificatePinningConfig(rawValue = process.env.ELECTRON_CERT_PINS) {
  if (rawValue === undefined || rawValue === null || String(rawValue).trim() === '') {
    return [];
  }

  let parsed;

  try {
    parsed = JSON.parse(String(rawValue));
  } catch (error) {
    throw new Error(`ELECTRON_CERT_PINS must be valid JSON: ${error instanceof Error ? error.message : 'Unable to parse.'}`);
  }

  const rules = Array.isArray(parsed) ? parsed : [parsed];
  return rules.map(parseCertificatePinRule);
}

function matchesPinnedHost(rule, hostname) {
  const normalizedHostname = normalizeHostname(hostname);

  if (normalizedHostname === rule.host) {
    return true;
  }

  return rule.includeSubdomains && normalizedHostname.endsWith(`.${rule.host}`);
}

function getCertificateRawData(certificate) {
  if (!certificate) {
    return null;
  }

  if (Buffer.isBuffer(certificate.data)) {
    return certificate.data;
  }

  if (typeof certificate.data === 'string' && certificate.data.trim() !== '') {
    return Buffer.from(certificate.data, 'utf8');
  }

  if (typeof certificate.pem === 'string' && certificate.pem.trim() !== '') {
    return certificate.pem;
  }

  return null;
}

function getObservedCertificatePins(certificate) {
  const observed = {
    fingerprint256: typeof certificate?.fingerprint256 === 'string' ? normalizeFingerprint256(certificate.fingerprint256) : null,
    spkiSha256: null,
  };
  const rawCertificate = getCertificateRawData(certificate);

  if (!rawCertificate) {
    return observed;
  }

  try {
    const x509 = new X509Certificate(rawCertificate);
    const spkiDer = x509.publicKey.export({ format: 'der', type: 'spki' });
    observed.spkiSha256 = createHash('sha256').update(spkiDer).digest('base64');
  } catch {
    observed.spkiSha256 = null;
  }

  return observed;
}

function verifyPinnedCertificate(rule, certificate) {
  const observed = getObservedCertificatePins(certificate);
  const fingerprintMatch = observed.fingerprint256 && rule.fingerprint256.includes(observed.fingerprint256);
  const spkiMatch = observed.spkiSha256 && rule.spkiSha256.includes(observed.spkiSha256);

  return {
    ok: Boolean(fingerprintMatch || spkiMatch),
    observed,
  };
}

function installCertificatePinning(targetSession, { logger = console, rawConfig } = {}) {
  const rules = loadCertificatePinningConfig(rawConfig);

  if (!targetSession || typeof targetSession.setCertificateVerifyProc !== 'function') {
    throw new Error('A valid Electron session is required to install certificate pinning.');
  }

  if (rules.length === 0) {
    return {
      enabled: false,
      rules,
    };
  }

  targetSession.setCertificateVerifyProc((request, callback) => {
    const rule = rules.find((candidate) => matchesPinnedHost(candidate, request.hostname));

    if (!rule) {
      callback(-3);
      return;
    }

    if (request.verificationResult !== 'OK') {
      logger.error('[electron] certificate verification failed before pin validation', {
        hostname: request.hostname,
        verificationResult: request.verificationResult,
        errorCode: request.errorCode,
      });
      callback(-2);
      return;
    }

    const verification = verifyPinnedCertificate(rule, request.certificate);

    if (verification.ok) {
      callback(0);
      return;
    }

    logger.error('[electron] certificate pin validation failed', {
      hostname: request.hostname,
      expectedFingerprint256: rule.fingerprint256,
      expectedSpkiSha256: rule.spkiSha256,
      observedFingerprint256: verification.observed.fingerprint256,
      observedSpkiSha256: verification.observed.spkiSha256,
    });
    callback(-2);
  });

  return {
    enabled: true,
    rules,
  };
}

module.exports = {
  installCertificatePinning,
  loadCertificatePinningConfig,
  matchesPinnedHost,
};
