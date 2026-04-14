import { Capacitor } from '@capacitor/core';

interface CameraPhoto {
  base64String?: string;
  dataUrl?: string;
  path?: string;
  webPath?: string;
  format: string;
  exifRotation?: number;
}

interface CameraPlugin {
  getPhoto(options: {
    quality: number;
    allowEditing: boolean;
    resultType: string;
    source?: string;
    width?: number;
    height?: number;
    correctOrientation?: boolean;
    saveToGallery?: boolean;
    promptLabelHeader?: string;
    promptLabelPhoto?: string;
    promptLabelPicture?: string;
  }): Promise<CameraPhoto>;
  checkPermissions(): Promise<{ camera: string; photos: string }>;
  requestPermissions(permissions?: { permissions: string[] }): Promise<{ camera: string; photos: string }>;
}

let cameraPlugin: CameraPlugin | null = null;

async function getCameraPlugin(): Promise<CameraPlugin | null> {
  if (!Capacitor.isNativePlatform()) return null;

  if (cameraPlugin) return cameraPlugin;

  try {
    const mod = await import('@capacitor/camera');
    cameraPlugin = mod.Camera as unknown as CameraPlugin;
    return cameraPlugin;
  } catch {
    return null;
  }
}

export async function isCameraAvailable(): Promise<boolean> {
  return Capacitor.isNativePlatform() && (await getCameraPlugin()) !== null;
}

export async function checkCameraPermission(): Promise<'granted' | 'denied' | 'prompt'> {
  const plugin = await getCameraPlugin();
  if (!plugin) return 'denied';

  try {
    const result = await plugin.checkPermissions();
    return result.camera as 'granted' | 'denied' | 'prompt';
  } catch {
    return 'denied';
  }
}

export async function requestCameraPermission(): Promise<boolean> {
  const plugin = await getCameraPlugin();
  if (!plugin) return false;

  try {
    const result = await plugin.requestPermissions({ permissions: ['camera', 'photos'] });
    return result.camera === 'granted';
  } catch {
    return false;
  }
}

export async function takePhoto(options?: {
  quality?: number;
  width?: number;
  height?: number;
  allowEditing?: boolean;
}): Promise<{ dataUrl: string; format: string } | null> {
  const plugin = await getCameraPlugin();
  if (!plugin) return null;

  try {
    const photo = await plugin.getPhoto({
      quality: options?.quality ?? 80,
      allowEditing: options?.allowEditing ?? true,
      resultType: 'dataUrl',
      source: 'CAMERA',
      width: options?.width ?? 1024,
      height: options?.height ?? 1024,
      correctOrientation: true,
      promptLabelHeader: 'Photo',
      promptLabelPhoto: 'Take Photo',
      promptLabelPicture: 'Choose from Gallery',
    });
    return { dataUrl: photo.dataUrl ?? '', format: photo.format };
  } catch {
    return null;
  }
}

export async function pickFromGallery(options?: {
  quality?: number;
  width?: number;
  height?: number;
}): Promise<{ dataUrl: string; format: string } | null> {
  const plugin = await getCameraPlugin();
  if (!plugin) return null;

  try {
    const photo = await plugin.getPhoto({
      quality: options?.quality ?? 80,
      allowEditing: false,
      resultType: 'dataUrl',
      source: 'PHOTOS',
      width: options?.width ?? 1024,
      height: options?.height ?? 1024,
      correctOrientation: true,
    });
    return { dataUrl: photo.dataUrl ?? '', format: photo.format };
  } catch {
    return null;
  }
}

export async function captureProfilePhoto(): Promise<string | null> {
  const result = await takePhoto({ quality: 90, width: 512, height: 512, allowEditing: true });
  return result?.dataUrl ?? null;
}

export async function captureDocument(): Promise<string | null> {
  const result = await takePhoto({ quality: 95, width: 2048, height: 2048, allowEditing: false });
  return result?.dataUrl ?? null;
}
