// K7. The log is drawn newest first, so the newest line is at the top of the box.

/** How close to the top still counts as reading the newest line. */
const SLACK_PX = 4;

/** Whether the reader is at the newest line rather than scrolled away to an older one. */
export function isAtNewest(box) {
    return !box || box.scrollTop <= SLACK_PX;
}

/** Back to the newest line. */
export function toNewest(box) {
    if (box) {
        box.scrollTop = 0;
    }
}
