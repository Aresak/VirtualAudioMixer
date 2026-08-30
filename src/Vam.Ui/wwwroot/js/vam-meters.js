// The meters, drawn to canvas.
//
// F1 to F7 live here rather than in the render tree, and that is the single most important decision
// in the client. Sixteen strips at twenty-five frames a second is four hundred component updates a
// second; under Blazor Server every one of those is a diff and a message over a socket, and under a
// WebView it is enough churn to make a fader feel like it is lagging. The engine's meter frames come
// in as one packed byte array and are drawn straight onto a canvas that Blazor never touches.
//
// The layout decoded here is MeterFrameCodec's. If one changes, both change: ten bytes a strip,
// four a bus, decibels in hundredths as signed sixteen-bit little-endian.

const CHANNEL_BYTES = 10;
const BUS_BYTES = 4;

// The meter's range. Zero sits near the top with headroom above it, because a meter that puts unity
// at the ceiling gives an operator nowhere to see an overshoot happening.
const FLOOR_DB = -60;
const CEILING_DB = 6;

// How far gain reduction has to go before the strip is drawn full. Six decibels of reduction is a
// lot on a voice, and a scale that ran to thirty would leave normal compression invisible.
const GR_RANGE_DB = 12;

const FLAG_MUTED = 1;
const FLAG_SOLOED = 2;
const FLAG_FAULTED = 4;
const FLAG_DUCKED = 8;
const FLAG_ABSENT = 16;
const FLAG_CLIPPED = 32;

// Peak hold. A peak that vanished in forty milliseconds is a peak nobody saw.
const HOLD_MS = 1200;
const HOLD_FALL_DB_PER_S = 20;

const state = {
    channels: [],
    buses: [],
    holds: [],
    lastFrame: 0
};

// .NET hands a byte[] across as a Uint8Array where the host supports it and as base64 where it does
// not. Both arrive here, and neither is worth a branch anywhere further in.
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

function readInt16(view, offset) {
    return view.getInt16(offset, true) / 100;
}

function readUint16(view, offset) {
    return view.getUint16(offset, true) / 65535;
}

/** Where a level sits in a meter, from 0 at the floor to 1 at the ceiling. */
function normalise(db) {
    if (!isFinite(db)) {
        return 0;
    }

    const clamped = Math.min(Math.max(db, FLOOR_DB), CEILING_DB);

    return (clamped - FLOOR_DB) / (CEILING_DB - FLOOR_DB);
}

/** Sizes a canvas to its box in device pixels, so meters are not blurry on a scaled display. */
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

function verticalGradient(context, height, ducked) {
    const gradient = context.createLinearGradient(0, 0, 0, height);

    if (ducked) {
        // Held at the automix floor. Still making sound, none of it reaching the mix. Grey says that
        // without pretending the signal has gone away.
        gradient.addColorStop(0, '#5c6874');
        gradient.addColorStop(1, '#39434e');
        return gradient;
    }

    gradient.addColorStop(0, '#e0503c');
    gradient.addColorStop(0.12, '#e5a32e');
    gradient.addColorStop(0.28, '#c9c34a');
    gradient.addColorStop(0.5, '#4fb477');
    gradient.addColorStop(1, '#4fb477');

    return gradient;
}

function drawTicks(context, width, height) {
    const ratio = window.devicePixelRatio || 1;

    context.fillStyle = 'rgba(0,0,0,0.55)';

    for (let y = 11 * ratio; y < height; y += 12 * ratio) {
        context.fillRect(0, y, width, Math.max(1, ratio));
    }
}

function drawMeter(canvas, peakDb, rmsDb, hold, flags) {
    const context = fit(canvas);

    if (!context) {
        return;
    }

    const width = canvas.width;
    const height = canvas.height;
    const ratio = window.devicePixelRatio || 1;

    context.clearRect(0, 0, width, height);
    context.fillStyle = '#0a0d10';
    context.fillRect(0, 0, width, height);

    // Absent or faulted draws nothing moving. A meter twitching on a channel whose device is gone is
    // the console telling an operator a lie about the room.
    if ((flags & (FLAG_ABSENT | FLAG_FAULTED)) !== 0) {
        context.fillStyle = flags & FLAG_FAULTED ? 'rgba(224,80,60,0.25)' : 'rgba(117,131,143,0.16)';
        context.fillRect(0, 0, width, height);
        return;
    }

    // Average as the body of the bar and peak as the line above it. One says how loud it sounded,
    // the other whether anything clipped, and a meter that shows only one leaves the other a guess.
    const rms = normalise(rmsDb) * height;

    context.fillStyle = verticalGradient(context, height, (flags & FLAG_DUCKED) !== 0);
    context.fillRect(0, height - rms, width, rms);

    drawTicks(context, width, height);

    const peak = normalise(peakDb) * height;

    if (peak > 0) {
        context.fillStyle = 'rgba(228,232,234,0.5)';
        context.fillRect(0, height - peak, width, Math.max(1, ratio));
    }

    if (hold > FLOOR_DB) {
        const held = normalise(hold) * height;

        context.fillStyle = hold >= 0 ? '#e0503c' : 'rgba(228,232,234,0.85)';
        context.fillRect(0, Math.max(0, height - held - ratio), width, Math.max(1, ratio * 1.5));
    }

    // F1. Latched, and drawn as a solid cap at the top rather than as a colour somewhere in the bar.
    // A clip is one block in four hundred; an operator watching sixteen strips has no chance of
    // catching it as it happens, so it stays lit until they clear it.
    if ((flags & FLAG_CLIPPED) !== 0) {
        context.fillStyle = '#e0503c';
        context.fillRect(0, 0, width, Math.max(3, ratio * 3));
    }
}

function drawGainReduction(canvas, gainReductionDb) {
    const context = fit(canvas);

    if (!context) {
        return;
    }

    const width = canvas.width;
    const height = canvas.height;

    context.clearRect(0, 0, width, height);
    context.fillStyle = '#0a0d10';
    context.fillRect(0, 0, width, height);

    // Downwards from the top, because that is the direction the gain went.
    const amount = Math.min(Math.max(-gainReductionDb, 0), GR_RANGE_DB) / GR_RANGE_DB;

    if (amount <= 0) {
        return;
    }

    context.fillStyle = 'rgba(229,163,46,0.85)';
    context.fillRect(0, 0, width, amount * height);
}

function updateHold(index, peakDb, elapsedSeconds) {
    let hold = state.holds[index];

    if (!hold || peakDb >= hold.value) {
        state.holds[index] = { value: peakDb, age: 0 };
        return peakDb;
    }

    hold.age += elapsedSeconds;

    if (hold.age * 1000 > HOLD_MS) {
        hold.value -= HOLD_FALL_DB_PER_S * elapsedSeconds;
    }

    return hold.value;
}

/**
 * Collects the canvases the mixer view has just rendered.
 *
 * Called after every render that changes how many strips there are, and never per frame.
 */
export function bind() {
    state.channels = [];
    state.buses = [];

    document.querySelectorAll('canvas[data-vam-meter]').forEach(canvas => {
        const index = Number(canvas.dataset.vamMeter);

        state.channels[index] = state.channels[index] || {};
        state.channels[index].meter = canvas;
    });

    document.querySelectorAll('canvas[data-vam-gr]').forEach(canvas => {
        const index = Number(canvas.dataset.vamGr);

        state.channels[index] = state.channels[index] || {};
        state.channels[index].gr = canvas;
    });

    // The share bar and the speaking dot arrive in the same frame as the levels and change just as
    // often, so they are written here too rather than bound. They are two style writes a frame.
    document.querySelectorAll('[data-vam-share]').forEach(element => {
        const index = Number(element.dataset.vamShare);

        state.channels[index] = state.channels[index] || {};
        state.channels[index].share = element;
    });

    document.querySelectorAll('[data-vam-speaking]').forEach(element => {
        const index = Number(element.dataset.vamSpeaking);

        state.channels[index] = state.channels[index] || {};
        state.channels[index].speaking = element;
    });

    document.querySelectorAll('canvas[data-vam-bus]').forEach(canvas => {
        state.buses[Number(canvas.dataset.vamBus)] = canvas;
    });

    state.holds = [];
}

/**
 * One meter frame. The only thing on this path per frame, and it touches no DOM node Blazor owns.
 *
 * @param {Uint8Array|string} payload the packed frame
 * @param {number} channelCount strips in the frame
 * @param {number} busCount buses in the frame
 */
export function frame(payload, channelCount, busCount) {
    const bytes = toBytes(payload);
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    const now = performance.now();
    const elapsed = state.lastFrame === 0 ? 0.04 : Math.min((now - state.lastFrame) / 1000, 0.5);

    state.lastFrame = now;

    for (let index = 0; index < channelCount; index++) {
        const target = state.channels[index];

        if (!target) {
            continue;
        }

        const at = index * CHANNEL_BYTES;
        const peakDb = readInt16(view, at);
        const rmsDb = readInt16(view, at + 2);
        const gainReductionDb = readInt16(view, at + 4);
        const flags = bytes[at + 8];
        const hold = updateHold(index, peakDb, elapsed);

        if (target.meter) {
            drawMeter(target.meter, peakDb, rmsDb, hold, flags);
        }

        if (target.gr) {
            drawGainReduction(target.gr, gainReductionDb);
        }

        if (target.share) {
            const share = readUint16(view, at + 6);

            target.share.style.width = (share * 100).toFixed(1) + '%';
        }

        if (target.speaking) {
            // Speaking is holding a real share of the automixer's gain, not merely being above a
            // threshold. A microphone picking up the room from four metres away is not speaking.
            target.speaking.classList.toggle('on', readUint16(view, at + 6) > 0.12);
        }
    }

    const busBase = channelCount * CHANNEL_BYTES;

    for (let index = 0; index < busCount; index++) {
        const canvas = state.buses[index];

        if (!canvas) {
            continue;
        }

        const at = busBase + (index * BUS_BYTES);

        drawMeter(canvas, readInt16(view, at), readInt16(view, at + 2), FLOOR_DB, 0);
    }
}

/** Lets go of everything, so a view that has navigated away stops being drawn into. */
export function unbind() {
    state.channels = [];
    state.buses = [];
    state.holds = [];
}
