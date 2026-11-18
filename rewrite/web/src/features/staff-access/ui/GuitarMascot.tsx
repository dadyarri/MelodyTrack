import type { CSSProperties } from "react";
import styles from "./GuitarMascot.module.css";

type GuitarMascotProps = {
  attention: "idle" | "login" | "password" | "passkey" | "success";
  caret: number;
  passwordVisible: boolean;
};

/** Decorative: receives interaction state, never credentials. */
export function GuitarMascot({ attention, caret, passwordVisible }: GuitarMascotProps) {
  const pose = attention === "password" ? (passwordVisible ? "peek" : "cover") : attention;
  const look = attention === "login" ? Math.min(1, Math.max(0, caret / 24)) * 2 - 1 : 0;
  const style = {
    "--guitar-look-x": `${look * 5}px`,
    "--guitar-look-y": attention === "login" ? "3px" : "0px",
    "--guitar-tilt": `${look * 3}deg`,
  } as CSSProperties;

  return (
    <div className={styles.mascot} data-pose={pose} style={style} aria-hidden="true">
      <svg viewBox="0 0 280 240" fill="none" focusable="false" aria-hidden="true">
        <ellipse className={styles.shadow} cx="140" cy="226" rx="57" ry="6" />
        <g className={styles.character} strokeLinecap="round" strokeLinejoin="round">
          <g className={styles.feet}>
            <path d="M122 210l-5 13h-13M158 210l5 13h13" />
          </g>
          <g className={styles.wood}>
            <path d="M129 52h22l-2 58h-18z" />
            <path d="M128 20q12-5 24 0l3 31q-15 7-30 0z" />
          </g>
          <g className={styles.hardware}>
            <path d="M127 27h-8m8 13h-8m34-13h8m-8 13h8" />
            <path d="M132 64h16m-16 12h16m-16 12h16m-16 12h16" />
          </g>
          <path
            className={styles.body}
            d="M140 103c-22-19-53-4-51 23 1 15 12 21 5 32-11 15-18 24-13 40 7 26 39 24 59 24s52 2 59-24c5-16-2-25-13-40-7-11 4-17 5-32 2-27-29-42-51-23Z"
          />
          <path
            className={styles.inlay}
            d="M140 111c-19-18-46-6-43 15 2 16 13 23 4 36-12 16-16 23-12 34 6 19 33 18 51 18s45 1 51-18c4-11 0-18-12-34-9-13 2-20 4-36 3-21-24-33-43-15Z"
          />
          <path className={styles.grain} d="M99 184q-8 17 13 21m65-95q10 5 8 16" />
          <circle className={styles.rosette} cx="140" cy="169" r="21" />
          <circle className={styles.soundhole} cx="140" cy="169" r="16" />
          <g className={styles.strings}>
            <path d="M137 27v175m3-175v175m3-175v175" />
          </g>
          <path className={styles.bridge} d="M126 201h28" />
          <g>
            <ellipse className={styles.cheek} cx="109" cy="144" rx="7" ry="4" />
            <ellipse className={styles.cheek} cx="171" cy="144" rx="7" ry="4" />
            <ellipse className={styles.eyeWhite} cx="120" cy="132" rx="10" ry="12" />
            <ellipse className={styles.eyeWhite} cx="160" cy="132" rx="10" ry="12" />
            <g className={styles.pupils}>
              <ellipse className={styles.pupil} cx="120" cy="133" rx="4" ry="6" />
              <ellipse className={styles.pupil} cx="160" cy="133" rx="4" ry="6" />
              <circle className={styles.glint} cx="121" cy="131" r="1.5" />
              <circle className={styles.glint} cx="161" cy="131" r="1.5" />
            </g>
            <path className={styles.closedEyes} d="M113 132q7 6 14 0m26 0q7 6 14 0" />
            <path className={styles.smile} d="M132 168q8 10 16 0" />
            <path className={styles.brows} d="m111 114 12-2m34 0 12 2" />
          </g>
          <g className={styles.leftArm}>
            <path className={styles.arm} d="M95 153q-20 15-29-6" />
            <path
              className={styles.hand}
              d="M60 149c-5-4-7-13-3-15 3-1 4 6 5 5-2-10 2-13 5-4 1-9 6-8 5 1 5-7 8-3 4 3l-4 8q-5 7-12 2Z"
            />
          </g>
          <g className={styles.rightArm}>
            <path className={styles.arm} d="M185 153q20 15 29-6" />
            <path
              className={styles.hand}
              d="M220 149c5-4 7-13 3-15-3-1-4 6-5 5 2-10-2-13-5-4-1-9-6-8-5 1-5-7-8-3-4 3l4 8q5 7 12 2Z"
            />
            <g className={styles.key}>
              <circle cx="224" cy="102" r="15" />
              <circle className={styles.keyHole} cx="224" cy="102" r="5" />
              <path className={styles.keyOutline} d="M224 117v30h10m-10-12h9" />
              <path className={styles.keyShaft} d="M224 117v30h10m-10-12h9" />
            </g>
          </g>
        </g>
      </svg>
    </div>
  );
}
