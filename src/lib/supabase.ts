import { createClient, type SupabaseClient } from '@supabase/supabase-js';

/**
 * Lazy Supabase client. Used for cloud saves (M5). Returns null if env vars
 * aren't set so the app still boots locally without a backend configured.
 *
 * Set VITE_SUPABASE_URL and VITE_SUPABASE_ANON_KEY in a .env file (see .env.example).
 */
let client: SupabaseClient | null = null;

export function getSupabase(): SupabaseClient | null {
  if (client) return client;
  const url = import.meta.env.VITE_SUPABASE_URL;
  const key = import.meta.env.VITE_SUPABASE_ANON_KEY;
  if (!url || !key) {
    console.warn('[supabase] env vars not set — running without cloud saves.');
    return null;
  }
  client = createClient(url, key);
  return client;
}
