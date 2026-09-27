import { once } from 'node:events';
import { describe, expect, it } from 'vitest';
import {
  createEventFilter,
  filterContainerList,
  filterImageList,
  filterNetworkInspect,
  filterNetworkList,
  filterVolumeList,
  isVisibleEvent,
  redactContainerInspect,
} from '../src/filters.js';

describe('list filters', () => {
  it('shows only OET containers and hides the console', () => {
    const list = [
      { Id: '1', Names: ['/oet-api-blue'] },
      { Id: '2', Names: ['/oet-agent-console'] },
      { Id: '3', Names: ['/ubag-vps-gateway-1'] },
      { Id: '4', Names: [] },
      { Id: '5', Names: ['/oetwebsite-legacy'] },
      { Id: '6', Names: ['/oet-agent-gateway'] },
    ];
    expect((filterContainerList(list) as Array<{ Id: string }>).map((c) => c.Id)).toEqual(['1', '5']);
  });

  it('keeps only project images', () => {
    const list = [
      { Id: 'a', RepoTags: ['ghcr.io/jerryboganda/oetwebapp-api:sha'] },
      { Id: 'b', RepoTags: ['pgvector/pgvector:pg17'] },
      { Id: 'c', RepoTags: [] },
      { Id: 'd', RepoTags: null },
      { Id: 'e', RepoTags: ['oetwebsite-learner-api:local'] },
    ];
    expect((filterImageList(list) as Array<{ Id: string }>).map((i) => i.Id)).toEqual(['a', 'e']);
  });

  it('keeps only OET networks and never the console networks', () => {
    const list = [{ Name: 'oetwebsite_internal' }, { Name: 'oet_agent_net' }, { Name: 'bridge' }, { Name: 'npm_proxy' }];
    expect(filterNetworkList(list)).toEqual([{ Name: 'oetwebsite_internal' }]);
  });

  it('hides non-OET containers attached to an OET network', () => {
    const doc = {
      Name: 'oetwebsite_internal',
      Containers: {
        a: { Name: 'oet-postgres' },
        b: { Name: 'ubag-vps-gateway-1' },
        c: { Name: 'oet-agent-dbproxy' },
      },
    };
    expect(filterNetworkInspect(doc)).toEqual({ Name: 'oetwebsite_internal', Containers: { a: { Name: 'oet-postgres' } } });
  });

  it('keeps only OET volumes and never the console volumes', () => {
    const doc = {
      Volumes: [{ Name: 'oetwebsite_oet_postgres_data' }, { Name: 'oet-agent-console_oet_agent_home' }, { Name: 'cotenant_data' }],
      Warnings: null,
    };
    expect(filterVolumeList(doc)).toEqual({ Volumes: [{ Name: 'oetwebsite_oet_postgres_data' }], Warnings: null });
  });

  it('passes non-array bodies through untouched', () => {
    expect(filterContainerList({ message: 'x' })).toEqual({ message: 'x' });
    expect(filterImageList('x')).toBe('x');
  });
});

describe('redactContainerInspect', () => {
  it('replaces every Config.Env value but keeps the keys', () => {
    const doc = { Id: 'x', Name: '/oet-api-blue', Config: { Env: ['A=1', 'B=two=2', 'C', 42] } };
    expect(redactContainerInspect(doc)).toEqual({
      Id: 'x',
      Name: '/oet-api-blue',
      Config: { Env: ['A=<redacted>', 'B=<redacted>', 'C', '<redacted>'] },
    });
  });

  it('tolerates documents without Config.Env', () => {
    expect(redactContainerInspect({ Id: 'x' })).toEqual({ Id: 'x' });
    expect(redactContainerInspect(null)).toBeNull();
  });
});

describe('events', () => {
  it.each([
    [{ Type: 'container', Actor: { ID: '1', Attributes: { name: 'oet-api-blue' } } }, true],
    [{ Type: 'container', Actor: { ID: '1', Attributes: { name: 'oet-agent-console' } } }, false],
    [{ Type: 'container', Actor: { ID: '1', Attributes: { name: 'ubag-vps-gateway-1' } } }, false],
    [{ Type: 'network', Actor: { ID: 'n', Attributes: { name: 'oetwebsite_internal', container: 'abc' } } }, true],
    [{ Type: 'network', Actor: { ID: 'n', Attributes: { name: 'oet_agent_net', container: 'abc' } } }, false],
    [{ Type: 'volume', Actor: { ID: 'oetwebsite_oet_db_backups', Attributes: {} } }, true],
    [{ Type: 'volume', Actor: { ID: 'oet-agent-console_oet_agent_sessions', Attributes: {} } }, false],
    [{ Type: 'image', Actor: { ID: 'ghcr.io/jerryboganda/oetwebapp-api:sha', Attributes: {} } }, true],
    [{ Type: 'daemon', Actor: { ID: 'x', Attributes: { name: 'oet-host' } } }, false],
  ])('isVisibleEvent(%j) === %s', (event, expected) => {
    expect(isVisibleEvent(event)).toBe(expected);
  });

  it('filters a chunked JSON-lines stream', async () => {
    const visible1 = JSON.stringify({ Type: 'container', Actor: { Attributes: { name: 'oet-api-blue' } } });
    const hidden = JSON.stringify({ Type: 'container', Actor: { Attributes: { name: 'cotenant' } } });
    const visible2 = JSON.stringify({ Type: 'container', Actor: { Attributes: { name: 'oet-web-blue' } } });
    const filter = createEventFilter();
    const out: string[] = [];
    filter.on('data', (chunk: Buffer | string) => out.push(chunk.toString()));
    const stream = `${visible1}\n${hidden}\nnot-json\n${visible2}`;
    filter.write(Buffer.from(stream.slice(0, 30)));
    filter.write(Buffer.from(stream.slice(30, 95)));
    filter.end(Buffer.from(stream.slice(95)));
    await once(filter, 'end');
    expect(out.join('')).toBe(`${visible1}\n${visible2}\n`);
  });
});
