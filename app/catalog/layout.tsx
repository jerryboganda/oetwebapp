import { IosPurchaseGate } from '@/components/compliance/ios-purchase-gate';

/**
 * App Store compliance (handover 4D): inside the iOS shell this route shows an
 * "enrol on our website" notice instead of a purchase surface. No in-app
 * purchases anywhere in the iOS app; web/Android/desktop are unaffected.
 * See docs/IOS-PURCHASE-COMPLIANCE.md.
 */
export default function Layout({ children }: { children: React.ReactNode }) {
  return <IosPurchaseGate>{children}</IosPurchaseGate>;
}
