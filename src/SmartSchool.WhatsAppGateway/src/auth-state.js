import { initAuthCreds, proto } from '@whiskeysockets/baileys';
import { EncryptedJsonStore } from './encrypted-store.js';

function emptyState() {
  return { version: 1, creds: initAuthCreds(), keys: {} };
}

/**
 * Baileys-compatible encrypted authentication state. A single in-memory copy is
 * persisted atomically, avoiding the plaintext key-per-file helper intended for demos.
 */
export async function useEncryptedAuthState(filePath, key) {
  const store = new EncryptedJsonStore(filePath, key);
  let document = await store.load(emptyState);
  if (!document?.creds || !document?.keys) {
    throw new Error('The encrypted WhatsApp authentication state is malformed.');
  }

  const persist = () => store.save(document);

  return {
    state: {
      creds: document.creds,
      keys: {
        async get(type, ids) {
          const category = document.keys[type] || {};
          const result = {};
          for (const id of ids) {
            let value = category[id] ?? null;
            if (type === 'app-state-sync-key' && value) {
              value = proto.Message.AppStateSyncKeyData.fromObject(value);
            }
            result[id] = value;
          }
          return result;
        },
        async set(data) {
          for (const [categoryName, values] of Object.entries(data)) {
            const category = document.keys[categoryName] || (document.keys[categoryName] = {});
            for (const [id, value] of Object.entries(values)) {
              if (value === null || value === undefined) delete category[id];
              else category[id] = value;
            }
            if (Object.keys(category).length === 0) delete document.keys[categoryName];
          }
          await persist();
        }
      }
    },
    get isRegistered() {
      return Boolean(document.creds.registered);
    },
    saveCreds: persist,
    async clear() {
      document = emptyState();
      await store.clear();
    }
  };
}
