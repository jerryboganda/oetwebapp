const BLOCKED_TAGS = ['script', 'style', 'iframe', 'object', 'embed', 'link', 'meta'];

function escapeHtml(input: string): string {
  return input
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

function sanitizeWithDomParser(html: string): string {
  const parser = new DOMParser();
  const doc = parser.parseFromString(html, 'text/html');

  BLOCKED_TAGS.forEach((tag) => {
    doc.querySelectorAll(tag).forEach((node) => node.remove());
  });

  doc.querySelectorAll('*').forEach((element) => {
    Array.from(element.attributes).forEach((attr) => {
      const name = attr.name.toLowerCase();
      const value = attr.value.trim().toLowerCase();
      if (name.startsWith('on')) {
        element.removeAttribute(attr.name);
      }
      if ((name === 'href' || name === 'src') && value.startsWith('javascript:')) {
        element.removeAttribute(attr.name);
      }
    });
  });

  return doc.body.innerHTML;
}

export function sanitizeRichHtml(html: string): string {
  if (!html) return '';
  if (typeof DOMParser !== 'undefined') {
    return sanitizeWithDomParser(html);
  }

  return escapeHtml(
    html
      .replace(/<script[\s\S]*?<\/script>/gi, '')
      .replace(/<style[\s\S]*?<\/style>/gi, '')
      .replace(/\son[a-z]+\s*=\s*("[^"]*"|'[^']*'|[^\s>]+)/gi, ''),
  );
}
