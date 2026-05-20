/**
 * Configuration for the OET Copilot Extension.
 * All values are read from environment variables with sensible defaults.
 */

export const config = {
  /** OET backend API base URL */
  API_URL: process.env.API_URL || 'http://localhost:5000',

  /** GitHub webhook secret for signature verification */
  WEBHOOK_SECRET: process.env.WEBHOOK_SECRET || '',

  /** Port for this extension service */
  PORT: parseInt(process.env.PORT || '3001', 10),

  /** Token used when calling OET backend copilot endpoints */
  COPILOT_BACKEND_TOKEN: process.env.COPILOT_BACKEND_TOKEN || '',

  /** Request timeout in ms for backend calls */
  BACKEND_TIMEOUT_MS: parseInt(process.env.BACKEND_TIMEOUT_MS || '30000', 10),
};
