import { createMemo } from 'solid-js';
import './ColorSwatch.css';

interface ColorSwatchProps {
  mode?: string;
  color?: string;
  alpha?: number;
  colorA?: string;
  alphaA?: number;
  colorB?: string;
  alphaB?: number;
  angle?: number;
}

function hexToRgba(hex: string, alpha: number): string {
  const r = parseInt(hex.slice(1, 3), 16);
  const g = parseInt(hex.slice(3, 5), 16);
  const b = parseInt(hex.slice(5, 7), 16);
  return `rgba(${r},${g},${b},${alpha})`;
}

export default function ColorSwatch(props: ColorSwatchProps) {
  const bg = createMemo(() => {
    if (props.mode === 'gradient') {
      const a = hexToRgba(props.colorA || '#60cdff', props.alphaA ?? 1);
      const b = hexToRgba(props.colorB || '#34d399', props.alphaB ?? 1);
      const deg = props.angle ?? 135;
      return `linear-gradient(${deg}deg, ${a}, ${b})`;
    }
    return hexToRgba(props.color || '#60cdff', props.alpha ?? 1);
  });

  return <div class="color-swatch" style={{ background: bg() }} />;
}
