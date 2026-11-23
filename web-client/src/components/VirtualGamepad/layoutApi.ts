import type { GamepadLayout } from './types';

/** 服务端保存的一份布局方案（名称为唯一键）。 */
export interface SavedLayout {
  name: string;
  layout: GamepadLayout;
  updatedAt: string;
}

const ENDPOINT = '/api/layouts';

async function readList(response: Response): Promise<SavedLayout[]> {
  if (!response.ok) {
    throw new Error(`HTTP ${response.status}`);
  }

  const data: unknown = await response.json();
  return Array.isArray(data) ? (data as SavedLayout[]) : [];
}

export function fetchSavedLayouts(): Promise<SavedLayout[]> {
  return fetch(ENDPOINT, { cache: 'no-store' }).then(readList);
}

/** 以名称保存当前布局；同名由服务端直接覆盖，返回覆盖后的完整列表。 */
export function saveLayoutToServer(name: string, layout: GamepadLayout): Promise<SavedLayout[]> {
  return fetch(ENDPOINT, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, layout })
  }).then(readList);
}
