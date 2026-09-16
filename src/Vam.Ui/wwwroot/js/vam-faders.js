// Makes every slider in the console behave like a fader instead of like a web page.
//
// A browser range input jumps to wherever the track was clicked. On a mixing console nothing
// teleports: you take hold of the cap and move it. The difference matters because the cost of an
// accidental click here is a microphone at the wrong level in front of a room, and because clicking
// near a strip on the way to something else is a thing people do constantly.
//
// So a press that does not land on the cap is refused, and a press that does behaves normally -
// including the drag that follows, which the browser handles once it has accepted the press.
//
// Keyboard control is untouched. Arrow keys, Home and End still work, which is the accessible path
// and the precise one.

// How far either side of the cap still counts as taking hold of it. A cap is about 14px; being
// exact about it would make the fader hard to grab, which is its own way of being wrong.
const GRAB_SLACK = 12;

function isOnCap(slider, event) {
    const box = slider.getBoundingClientRect();
    const vertical = box.height > box.width;

    const min = Number(slider.min || 0);
    const max = Number(slider.max || 100);
    const span = max - min;

    if (span <= 0) {
        return true;
    }

    const fraction = (Number(slider.value) - min) / span;

    // A cap cannot hang off either end, so the travel it moves along is shorter than the track by
    // one cap. Ignoring that puts the computed centre past the real one at the extremes, which is
    // exactly where a fader is parked when somebody is most likely to grab it.
    const capSize = vertical ? Math.min(box.width, 20) : Math.min(box.height, 20);
    const travel = (vertical ? box.height : box.width) - capSize;

    if (vertical) {
        // Vertical range inputs run bottom to top.
        const centre = box.bottom - (capSize / 2) - (fraction * travel);

        return Math.abs(event.clientY - centre) <= (capSize / 2) + GRAB_SLACK;
    }

    const centre = box.left + (capSize / 2) + (fraction * travel);

    return Math.abs(event.clientX - centre) <= (capSize / 2) + GRAB_SLACK;
}

function onPointerDown(event) {
    const slider = event.target;

    if (!(slider instanceof HTMLInputElement) || slider.type !== 'range' || slider.disabled) {
        return;
    }

    if (isOnCap(slider, event)) {
        return;
    }

    // Refused, but the slider still takes focus, so somebody who meant to adjust it can carry on
    // with the arrow keys rather than being left with a control that ignored them.
    event.preventDefault();
    slider.focus();
}

// One listener for the console rather than one per slider. Strips come and go as devices are added
// and removed, and a per-element listener is a per-element leak.
export function attach() {
    if (window.__vamFadersAttached) {
        return;
    }

    window.__vamFadersAttached = true;

    document.addEventListener('pointerdown', onPointerDown, true);
}
