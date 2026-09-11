package com.secondwindgames.runbeat;

import java.util.concurrent.atomic.AtomicInteger;

/** Fractional sample deadlines never accumulate integer-period rounding drift. No UI timers. */
public final class RhythmSynth {
    public interface BeatListener { void onBeat(long frame, int target); }
    private final int rate;
    private int target, steps, sound, age = 999999;
    private final AtomicInteger pendingTarget = new AtomicInteger();
    private final float volume;
    private long cursor, beats;
    private double nextBeat;
    public RhythmSynth(int rate, int target, int steps, int sound, float volume) {
        this.rate = rate; this.target = clamp(target); this.steps = steps == 2 ? 2 : 1;
        this.sound = Math.max(0, Math.min(2, sound)); this.volume = Math.max(0, Math.min(1, volume));
    }
    public static int clamp(int value) { return Math.max(100, Math.min(220, value)); }
    public static double intervalFrames(int rate, int target, int steps) { return rate * 60.0 * steps / target; }
    public void setTarget(int value) { pendingTarget.set(clamp(value)); }
    public long frames() { return cursor; }
    public long beats() { return beats; }
    public void render(short[] output, int count, BeatListener listener) {
        for (int i = 0; i < count; i++, cursor++) {
            if (cursor >= Math.round(nextBeat)) {
                int requested = pendingTarget.getAndSet(0);
                if (requested != 0) target = requested;
                age = 0; beats++;
                if (listener != null) listener.onBeat(cursor, target);
                nextBeat += intervalFrames(rate, target, steps);
            }
            double t = (double)age / rate, wave = 0;
            if (t < .05) {
                double attack = Math.min(1, t / .0015);
                if (sound == 0) wave = Math.sin(2 * Math.PI * 880 * t) * Math.exp(-110 * t);
                else if (sound == 1) wave = (Math.sin(2 * Math.PI * 1500 * t) + .4 * Math.sin(2 * Math.PI * 2310 * t)) * Math.exp(-170 * t);
                else wave = Math.sin(2 * Math.PI * (1200 * t + 8 * (1 - Math.exp(-100 * t)))) * Math.exp(-90 * t);
                wave *= attack * .48 * volume;
            }
            output[i] = (short)(wave * 32767); age++;
        }
    }
}
