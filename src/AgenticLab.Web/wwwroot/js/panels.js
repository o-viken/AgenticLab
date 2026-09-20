// Drag-to-resize support for the Flow page's docked panels.
// initResizer wires a splitter element so dragging it updates the parent .flow-body grid's
// --left-w / --right-w / --bottom-h CSS variable live, then reports the final pixel size back to .NET.
// Left/Right resize width (horizontal drag); Bottom resizes height (vertical drag).
window.agenticLabPanels = (function () {
    "use strict";

    const MIN_W = 240;
    const MAX_W = 640;
    const MIN_H = 120;
    const MAX_H = 900;

    function initResizer(handle, dotnetRef, side, sizeVariable) {
        if (!handle) {
            return;
        }

        const body = handle.closest(".flow-body");
        if (!body) {
            return;
        }

        const vertical = side === "bottom";
        const cssVar = sizeVariable || (vertical ? "--bottom-h" : side === "left" ? "--left-w" : "--right-w");
        const conversation = side === "left" && cssVar === "--left-w";
        const min = vertical ? MIN_H : MIN_W;
        const dragClass = vertical ? "resizing-y" : "resizing";

        let start = 0;
        let startSize = 0;
        let current = 0;
        let dragging = false;

        function readSize() {
            const bounds = handle.closest(".side-panel").getBoundingClientRect();
            return vertical ? bounds.height : bounds.width;
        }

        function maxSize() {
            if (vertical) return MAX_H;
            const available = conversation
                ? handle.closest(".workspace-grid").getBoundingClientRect().width * .65
                : body.getBoundingClientRect().width * .25;
            return Math.max(min, Math.min(conversation ? 960 : MAX_W, available));
        }

        function applySize(size) {
            current = Math.max(min, Math.min(maxSize(), size));
            body.style.setProperty(cssVar, conversation
                ? `minmax(0, min(${current}px, 65cqw))`
                : current + "px");
            updateAria();
        }

        function updateAria() {
            handle.setAttribute("aria-valuemin", min);
            handle.setAttribute("aria-valuemax", Math.round(maxSize()));
            handle.setAttribute("aria-valuenow", Math.round(readSize()));
        }

        function onMove(e) {
            if (!dragging) {
                return;
            }

            let size;
            if (vertical) {
                // The bottom panel's splitter is on its top edge, so dragging up grows it.
                const dy = e.clientY - start;
                size = startSize - dy;
            } else {
                const dx = e.clientX - start;
                // Left panel grows as the handle moves right; right panel grows as it moves left.
                size = side === "left" ? startSize + dx : startSize - dx;
            }

            applySize(size);
        }

        function onUp(e) {
            if (!dragging) {
                return;
            }

            dragging = false;
            body.classList.remove(dragClass);
            window.removeEventListener("pointermove", onMove);
            window.removeEventListener("pointerup", onUp);
            try {
                handle.releasePointerCapture(e.pointerId);
            } catch {
                /* capture may not have been set */
            }

            if (dotnetRef && current) {
                dotnetRef.invokeMethodAsync("OnResized", Math.round(current));
            }
        }

        function onDown(e) {
            if (e.button !== 0) return;
            dragging = true;
            start = vertical ? e.clientY : e.clientX;
            startSize = readSize();
            current = startSize;
            body.classList.add(dragClass);
            try {
                handle.setPointerCapture(e.pointerId);
            } catch {
                /* not all browsers support pointer capture on the element */
            }

            window.addEventListener("pointermove", onMove);
            window.addEventListener("pointerup", onUp);
            e.preventDefault();
        }

        function onKeyDown(event) {
            const increase = vertical ? "ArrowUp" : side === "left" ? "ArrowRight" : "ArrowLeft";
            const decrease = vertical ? "ArrowDown" : side === "left" ? "ArrowLeft" : "ArrowRight";
            if (![increase, decrease, "Home", "End"].includes(event.key)) return;
            event.preventDefault();
            const step = event.shiftKey ? 50 : 20;
            const size = event.key === "Home" ? min : event.key === "End" ? maxSize()
                : readSize() + (event.key === increase ? step : -step);
            applySize(size);
            dotnetRef.invokeMethodAsync("OnResized", Math.round(current));
        }

        dispose(handle);
        handle._tsDispose = function () {
            dragging = false;
            body.classList.remove(dragClass);
            handle.removeEventListener("pointerdown", onDown);
            handle.removeEventListener("keydown", onKeyDown);
            handle.removeEventListener("focus", updateAria);
            window.removeEventListener("pointermove", onMove);
            window.removeEventListener("pointerup", onUp);
        };
        handle.addEventListener("pointerdown", onDown);
        handle.addEventListener("keydown", onKeyDown);
        handle.addEventListener("focus", updateAria);
        updateAria();
    }

    function dispose(handle) {
        if (handle && handle._tsDispose) {
            handle._tsDispose();
            handle._tsDispose = null;
        }
    }

    // Keep a scroll container pinned to its newest content (the chat log), but only while the user is
    // already at/near the bottom. Once they scroll up to read earlier messages we stop yanking them
    // back down — so reading history isn't interrupted by re-renders (e.g. typing or a streaming reply).
    function stickToBottom(el) {
        if (!el || el.getClientRects().length === 0) {
            return;
        }

        if (!el._tsStickInit) {
            el._tsStickInit = true;
            el._tsStick = true;
            el.addEventListener("scroll", function () {
                const dist = el.scrollHeight - el.scrollTop - el.clientHeight;
                el._tsStick = dist < 48;
            });
        }

        if (el._tsStick) {
            el.scrollTop = el.scrollHeight;
        }
    }

    return { initResizer, dispose, stickToBottom };
})();

// Make multiline chat inputs submit on a plain Enter while keeping Shift+Enter for a new line.
// A textarea inserts a newline on Enter by default; for any element marked [data-enter-submit] we
// cancel that default when Enter is pressed without Shift. Blazor's own @onkeydown handler still
// fires to actually send, so no .NET callback is needed here. Registered once at module load.
(function () {
    "use strict";

    document.addEventListener("keydown", function (e) {
        const target = e.target;
        if (target?.matches?.('[role="tab"]') && target.closest(".tab-strip")
            && ["ArrowLeft", "ArrowRight", "Home", "End"].includes(e.key)) {
            e.preventDefault();
            return;
        }
        if (e.key !== "Enter" || e.shiftKey || e.isComposing) {
            return;
        }

        if (target && target.matches && target.matches("textarea[data-enter-submit]")) {
            e.preventDefault();
        }
    });
})();
