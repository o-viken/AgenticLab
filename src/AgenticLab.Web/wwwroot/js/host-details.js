const origins = new WeakMap();

export function activate(inspector) {
    const active = document.activeElement;
    const root = inspector.closest(".flow-app");
    if (root && active?.classList.contains("host-section")) origins.set(root, active);
    const heading = inspector.querySelector("#host-detail-heading");
    heading?.focus({ preventScroll: true });
    if (window.matchMedia("(max-width: 1199px)").matches) {
        inspector.scrollIntoView({ block: "start", behavior: "instant" });
    }
}

export function restore(inspector) {
    const trigger = origins.get(inspector.closest(".flow-app"));
    const target = trigger?.isConnected ? trigger
        : document.querySelector(".host-section") ?? document.querySelector(".view-options-trigger");
    target?.focus({ preventScroll: true });
    if (window.matchMedia("(max-width: 1199px)").matches) target?.scrollIntoView({ block: "center", behavior: "instant" });
}