// Makes dragging work at all.
//
// A drag whose dragstart handler sets no data on the DataTransfer is cancelled immediately by the
// browser - the pointer moves and nothing follows it, which is exactly what "it does not move" looks
// like. Blazor's @ondragstart hands the handler a read-only view of the event, so the data has to be
// set here instead.
//
// One delegated listener rather than one per row: strips and chain links are created and destroyed
// as devices and modifiers come and go, and a per-element listener is a per-element leak.

export function attach() {
    if (window.__vamDragAttached) {
        return;
    }

    window.__vamDragAttached = true;

    document.addEventListener('dragstart', event => {
        const transfer = event.dataTransfer;

        if (!transfer) {
            return;
        }

        try {
            // The payload is never read. What matters is that there is one, because a drag without
            // one does not start.
            transfer.setData('text/plain', 'vam');
            transfer.effectAllowed = 'move';
        } catch {
            // A browser that refuses is a browser where the drag was going to fail anyway.
        }
    }, true);
}
