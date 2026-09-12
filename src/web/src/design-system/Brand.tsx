import portalLogo from "../assets/portal-logo.png";

/** Sidebar header logo (Figma 175:161 "Portal logo", 137×46, object-cover). */
export function PortalLogo({ className = "" }: { className?: string }) {
  return <img src={portalLogo} alt="Портал поставщиков" className={`h-[46px] w-[137px] object-cover ${className}`} />;
}

/** Brand/Portal Mark (Figma 234:585): the mark without wordmark, cropped from the same logo asset —
 * the 137×46 image sits at (-7,-7) inside a 32×32 clip. For 24–32 px compact navigation states. */
export function PortalMark({ className = "" }: { className?: string }) {
  return (
    <span className={`relative block size-8 overflow-hidden ${className}`} aria-label="Портал поставщиков" role="img">
      <img src={portalLogo} alt="" className="absolute left-[-7px] top-[-7px] h-[46px] w-[137px] max-w-none object-cover" />
    </span>
  );
}
