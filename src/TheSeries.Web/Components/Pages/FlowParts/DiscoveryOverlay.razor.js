const dialogs = new WeakMap();

export function open(dialog, reference) {
    const trigger = document.activeElement;
    const overflow = document.body.style.overflow;
    const dismiss = event => {
        event.preventDefault();
        reference.invokeMethodAsync("CloseAsync");
    };
    const backdrop = event => {
        const bounds = dialog.getBoundingClientRect();
        if (event.target === dialog && (event.clientX < bounds.left || event.clientX > bounds.right ||
            event.clientY < bounds.top || event.clientY > bounds.bottom)) dismiss(event);
    };
    const keydown = event => {
        const focusedDialog = document.activeElement?.closest("dialog");
        if (event.key === "Escape" && dialog.matches(":modal") && (!focusedDialog || focusedDialog === dialog)) {
            event.stopPropagation();
            dismiss(event);
        }
    };
    dialog.addEventListener("cancel", dismiss);
    dialog.addEventListener("click", backdrop);
    document.addEventListener("keydown", keydown);
    dialogs.set(dialog, { trigger, overflow, dismiss, backdrop, keydown });
    document.body.style.overflow = "hidden";
    dialog.showModal();
}

export function close(dialog) {
    const state = dialogs.get(dialog);
    if (!state) return;
    dialog.removeEventListener("cancel", state.dismiss);
    dialog.removeEventListener("click", state.backdrop);
    document.removeEventListener("keydown", state.keydown);
    dialog.close();
    document.body.style.overflow = state.overflow;
    if (state.trigger?.isConnected) state.trigger.focus({ preventScroll: true });
    dialogs.delete(dialog);
}