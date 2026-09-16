// C9. The share band, drawn to canvas from the meter stream.
//
// It is here rather than in the render tree for the reason the meters are: the shares arrive
// twenty-five times a second, and a band redrawn through Blazor would diff a hundred and fifty
// rectangles that often. The band fed itself from the once-a-second console snapshot instead, which
// is why it was a staircase when it had anything in it at all.
//
// The layout decoded here is MeterFrameCodec's, the same as vam-meters.js: ten bytes a strip, with
// the automix gain in decibels at offset four and the share as a unit fraction at offset six.

const CHANNEL_BYTES = 10;

/** How much history the band holds. The heading says thirty seconds, so it is thirty seconds. */
const WINDOW_MS = 30000;

// A column narrower than a pixel is a column nobody sees, and keeping every frame of a long meeting
// would grow the ring without bound. Fifty a second is twice the frame rate and will never be hit.
const MAX_COLUMNS = WINDOW_MS / 20;

/** Below this a share is rounding noise, and drawing it puts a hairline under a channel that is out. */
const MIN_SHARE = 0.002;

const BACKGROUND = '#0a0d10';
const BORDER = '#2b343d';
const LABEL = '#75838f';

// The mockup's fill. Solid enough to read a handover at a glance, short of flat so the stack's
// boundaries stay visible where two channels are close.
const ALPHA = 0.88;

const state = {
    canvas: null,
    colours: [],
    columns: [],
    shares: [],
    gains: []
};

// .NET hands a byte[] across as a Uint8Array where the host supports it and as base64 where it does
// not. Both arrive here; neither is worth a branch further in.
function toBytes(payload) {
    if (typeof payload !== 'string') {
        return payload;
    }

    const binary = atob(payload);
    const bytes = new Uint8Array(binary.length);

    for (let index = 0; index < binary.length; index++) {
        bytes[index] = binary.charCodeAt(index);
    }

    return bytes;
}

/** Sizes a canvas to its box in device pixels, so the band is not blurry on a scaled display. */
function fit(canvas) {
    const ratio = window.devicePixelRatio || 1;
    const box = canvas.getBoundingClientRect();
    const width = Math.max(1, Math.round(box.width * ratio));
    const height = Math.max(1, Math.round(box.height * ratio));

    if (canvas.width !== width || canvas.height !== height) {
        canvas.width = width;
        canvas.height = height;
    }

    return canvas.getContext('2d');
}

function colourOf(index) {
    return state.colours[index] || LABEL;
}

/**
 * Where a column starts, as a fraction of the width.
 *
 * Placed by the time it was taken rather than by its position in the ring, so a console that
 * dropped frames draws a gap where the frames were missing instead of stretching what it did get
 * across the whole thirty seconds.
 */
function positionOf(timestamp, now) {
    return 1 - Math.min(Math.max((now - timestamp) / WINDOW_MS, 0), 1);
}

function drawScale(context, width, height) {
    const ratio = window.devicePixelRatio || 1;

    context.globalAlpha = 1;
    context.strokeStyle = BORDER;
    context.lineWidth = ratio;
    context.strokeRect(ratio / 2, ratio / 2, width - ratio, height - ratio);

    // Both ends named. A band with no axis is a picture of some colours, and a quiet meeting draws
    // an empty one that has to read as "nobody held the gain" rather than as a panel that is broken.
    context.fillStyle = LABEL;
    context.font = `${10 * ratio}px "IBM Plex Mono", ui-monospace, monospace`;
    context.textBaseline = 'alphabetic';
    context.fillText('−30 s', 6 * ratio, height - (6 * ratio));

    const now = 'now';

    context.fillText(now, width - context.measureText(now).width - (6 * ratio), height - (6 * ratio));
}

function draw() {
    if (!state.canvas) {
        return;
    }

    const context = fit(state.canvas);

    if (!context) {
        return;
    }

    const width = state.canvas.width;
    const height = state.canvas.height;
    const now = performance.now();

    context.clearRect(0, 0, width, height);
    context.fillStyle = BACKGROUND;
    context.fillRect(0, 0, width, height);

    for (let index = 0; index < state.columns.length; index++) {
        const column = state.columns[index];
        const next = state.columns[index + 1];
        const left = positionOf(column.timestamp, now) * width;
        const right = next ? positionOf(next.timestamp, now) * width : width;
        const span = Math.max(right - left, 1);

        let bottom = height;

        for (let channel = 0; channel < column.shares.length; channel++) {
            const share = column.shares[channel];

            if (share < MIN_SHARE) {
                continue;
            }

            const stacked = share * height;

            bottom -= stacked;

            context.globalAlpha = ALPHA;
            context.fillStyle = colourOf(channel);
            context.fillRect(left, bottom, span, stacked);
        }
    }

    drawScale(context, width, height);
}

function record(view, channelCount) {
    const shares = new Array(channelCount);

    for (let index = 0; index < channelCount; index++) {
        shares[index] = view.getUint16((index * CHANNEL_BYTES) + 6, true) / 65535;
    }

    state.columns.push({ timestamp: performance.now(), shares });

    const oldest = performance.now() - WINDOW_MS;

    while (state.columns.length > 0
        && (state.columns[0].timestamp < oldest || state.columns.length > MAX_COLUMNS)) {
        state.columns.shift();
    }
}

function writeRows(view, channelCount) {
    for (let index = 0; index < channelCount; index++) {
        const at = index * CHANNEL_BYTES;
        const bar = state.shares[index];
        const gain = state.gains[index];

        if (bar) {
            bar.style.width = ((view.getUint16(at + 6, true) / 65535) * 100).toFixed(1) + '%';
        }

        if (gain) {
            // The automixer's own gain, which the engine puts in the frame's gain-reduction field.
            gain.textContent = (view.getInt16(at + 4, true) / 100).toFixed(1) + ' dB';
        }
    }
}

/**
 * Collects the canvas and the channel rows the automix view has just rendered.
 *
 * Called after a render that changes how many channels there are, and never per frame.
 *
 * @param {Array<string>} colours the colour of each strip, in strip order
 */
export function bind(colours) {
    state.colours = colours || [];
    state.canvas = document.querySelector('canvas[data-vam-band]');
    state.shares = [];
    state.gains = [];

    document.querySelectorAll('[data-vam-axshare]').forEach(element => {
        state.shares[Number(element.dataset.vamAxshare)] = element;
    });

    document.querySelectorAll('[data-vam-axgain]').forEach(element => {
        state.gains[Number(element.dataset.vamAxgain)] = element;
    });

    draw();
}

/**
 * One meter frame. The only thing on this path per frame, and it touches no node Blazor owns.
 *
 * @param {Uint8Array|string} payload the packed frame
 * @param {number} channelCount strips in the frame
 */
export function frame(payload, channelCount) {
    const bytes = toBytes(payload);
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);

    record(view, channelCount);
    writeRows(view, channelCount);
    draw();
}

/** Lets go of everything, so a view that has navigated away stops being drawn into. */
export function unbind() {
    state.canvas = null;
    state.colours = [];
    state.columns = [];
    state.shares = [];
    state.gains = [];
}
