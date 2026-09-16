/// <reference types="vite/client" />
// Ambient declarations for non-TS assets imported for their side effects.
// Vite resolves these at build time; this tells `tsc --noEmit` they're valid
// modules so the type-check gate isn't drowned in spurious TS2882 errors.
declare module '*.css';
declare module '*.svg';
declare module '*.png';
declare module '*.jpg';
declare module '*.json';
