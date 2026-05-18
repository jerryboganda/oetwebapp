import { openCheckoutUrl } from './web-checkout';

const { mockFetchReleaseSettings, mockPurchaseNativeProduct, mockIsNativePlatform, mockGetPlatform } = vi.hoisted(() => ({
  mockFetchReleaseSettings: vi.fn(),
  mockPurchaseNativeProduct: vi.fn(),
  mockIsNativePlatform: vi.fn(),
  mockGetPlatform: vi.fn(),
}));

vi.mock('@capacitor/core', () => ({
  Capacitor: {
    isNativePlatform: mockIsNativePlatform,
    getPlatform: mockGetPlatform,
  },
}));

vi.mock('@/lib/api', () => ({
  fetchPublicAppReleaseSettings: mockFetchReleaseSettings,
}));

vi.mock('@/lib/mobile/in-app-purchases', () => ({
  purchaseConfiguredNativeProduct: mockPurchaseNativeProduct,
}));

describe('openCheckoutUrl', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockIsNativePlatform.mockReturnValue(true);
    mockGetPlatform.mockReturnValue('ios');
  });

  it('uses admin-configured native IAP on Capacitor when policy requires it', async () => {
    const releaseSettings = {
      platform: 'ios',
      billingPolicy: 'native-iap',
      revenueCatApiKey: 'appl_public',
      iapProductId: 'com.oetprep.monthly',
    };
    mockFetchReleaseSettings.mockResolvedValue(releaseSettings);
    mockPurchaseNativeProduct.mockResolvedValue('com.oetprep.monthly');

    await expect(openCheckoutUrl('https://checkout.example.test/session')).resolves.toBe('native-iap');

    expect(mockFetchReleaseSettings).toHaveBeenCalledWith('ios');
    expect(mockPurchaseNativeProduct).toHaveBeenCalledWith({
      releaseSettings,
      appUserId: undefined,
      productId: undefined,
    });
  });
});
