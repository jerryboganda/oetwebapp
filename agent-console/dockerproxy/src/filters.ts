// Response transforms applied on the policy path (never on control-plane
// requests). Pure except createEventFilter(), which wraps keepEventLine() in a
// stream Transform for the long-lived /events response.
import { Transform, type TransformCallback } from 'node:stream';
import { StringDecoder } from 'node:string_decoder';
import {
  CONSOLE_VOLUMES,
  isConsoleContainer,
  isOetImageRef,
  isVisibleContainerName,
  isVisibleNetworkName,
  isVisibleVolumeName,
  type ResponseTransform,
} from './policy.js';
import { asRecord, asStringArray, str, stripSlash } from './util.js';

export const REDACTED = '<redacted>';

/** GET /containers/json → only OET containers, never the console's own. */
export function filterContainerList(list: unknown): unknown {
  if (!Array.isArray(list)) return list;
  return list.filter((entry) => {
    const names = asStringArray(asRecord(entry).Names);
    return names.length > 0 && names.every((name) => isVisibleContainerName(name));
  });
}

/** GET /containers/{id}/json → Config.Env becomes ["KEY=<redacted>", …]. */
export function redactContainerInspect(doc: unknown): unknown {
  const config = asRecord(asRecord(doc).Config);
  if (Array.isArray(config.Env)) {
    config.Env = config.Env.map((entry: unknown) => {
      if (typeof entry !== 'string') return REDACTED;
      const eq = entry.indexOf('=');
      return eq >= 0 ? `${entry.slice(0, eq)}=${REDACTED}` : entry;
    });
  }
  return doc;
}

export function filterImageList(list: unknown): unknown {
  if (!Array.isArray(list)) return list;
  return list.filter((image) => asStringArray(asRecord(image).RepoTags).some(isOetImageRef));
}

export function filterNetworkList(list: unknown): unknown {
  if (!Array.isArray(list)) return list;
  return list.filter((network) => isVisibleNetworkName(str(asRecord(network).Name)));
}

/** GET /networks/{id} → hide attached containers that are not visible OET containers. */
export function filterNetworkInspect(doc: unknown): unknown {
  const record = asRecord(doc);
  const containers = asRecord(record.Containers);
  if (record.Containers !== undefined && record.Containers !== null) {
    const kept: Record<string, unknown> = {};
    for (const [id, value] of Object.entries(containers)) {
      if (isVisibleContainerName(str(asRecord(value).Name))) kept[id] = value;
    }
    record.Containers = kept;
  }
  return doc;
}

export function filterVolumeList(doc: unknown): unknown {
  const record = asRecord(doc);
  if (Array.isArray(record.Volumes)) {
    record.Volumes = record.Volumes.filter((volume: unknown) => isVisibleVolumeName(str(asRecord(volume).Name)));
  }
  return doc;
}

/** Event visibility for GET /events (one JSON object per line). */
export function isVisibleEvent(event: unknown): boolean {
  const e = asRecord(event);
  const type = str(e.Type) || str(e.type);
  const actor = asRecord(e.Actor);
  const attributes = asRecord(actor.Attributes);
  const name = str(attributes.name);
  switch (type) {
    case 'container':
      return name !== '' && isVisibleContainerName(name);
    case 'network':
      return name !== '' && isVisibleNetworkName(name) && !isConsoleContainer(stripSlash(str(attributes.container)));
    case 'volume': {
      const volume = str(actor.ID) || name;
      return volume !== '' && isVisibleVolumeName(volume) && !CONSOLE_VOLUMES.has(volume);
    }
    case 'image':
      return isOetImageRef(str(actor.ID)) || (name !== '' && isOetImageRef(name));
    default:
      return false;
  }
}

export function keepEventLine(line: string): boolean {
  const trimmed = line.trim();
  if (trimmed === '') return false;
  try {
    return isVisibleEvent(JSON.parse(trimmed));
  } catch {
    return false;
  }
}

export function createEventFilter(maxLineBytes = 1024 * 1024): Transform {
  const decoder = new StringDecoder('utf8');
  let pending = '';
  return new Transform({
    transform(chunk: Buffer, _encoding: BufferEncoding, callback: TransformCallback) {
      pending += decoder.write(chunk);
      const out: string[] = [];
      let newline = pending.indexOf('\n');
      while (newline >= 0) {
        const line = pending.slice(0, newline);
        pending = pending.slice(newline + 1);
        if (keepEventLine(line)) out.push(`${line.trim()}\n`);
        newline = pending.indexOf('\n');
      }
      if (pending.length > maxLineBytes) pending = '';
      callback(null, out.length > 0 ? out.join('') : undefined);
    },
    flush(callback: TransformCallback) {
      pending += decoder.end();
      callback(null, keepEventLine(pending) ? `${pending.trim()}\n` : undefined);
    },
  });
}

/** Applies a JSON transform to a fully-buffered daemon response. */
export function applyJsonTransform(transform: ResponseTransform, json: unknown): unknown {
  switch (transform) {
    case 'containers-list':
      return filterContainerList(json);
    case 'container-inspect':
      return redactContainerInspect(json);
    case 'images-list':
      return filterImageList(json);
    case 'networks-list':
      return filterNetworkList(json);
    case 'network-inspect':
      return filterNetworkInspect(json);
    case 'volumes-list':
      return filterVolumeList(json);
    case 'events':
    case 'exec-create':
      return json;
    default:
      return json;
  }
}
