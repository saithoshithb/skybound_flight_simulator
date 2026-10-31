import type { GraphicsQuality } from './settingsStore';

/**
 * What each graphics tier costs. Everything visual that scales with GPU or
 * CPU load is read from here, so a tier change is one switch.
 */
export interface GraphicsProfile {
  /** Device-pixel-ratio range given to the renderer. */
  dpr: [number, number];
  antialias: boolean;
  shadows: boolean;
  shadowMapSize: number;
  /** Terrain mesh grid (segments per side). Physics always uses the exact terrain function. */
  terrainSegments: number;
  trees: number;
  clouds: number;
  fogFar: number;
  /** Bloom and vignette allowed (also needs the Effects setting). */
  postFx: boolean;
}

export const GRAPHICS: Record<GraphicsQuality, GraphicsProfile> = {
  low: {
    dpr: [0.75, 1],
    antialias: false,
    shadows: false,
    shadowMapSize: 512,
    terrainSegments: 160,
    trees: 900,
    clouds: 16,
    fogFar: 14000,
    postFx: false,
  },
  medium: {
    dpr: [1, 1.5],
    antialias: true,
    shadows: true,
    shadowMapSize: 1024,
    terrainSegments: 280,
    trees: 1800,
    clouds: 28,
    fogFar: 19000,
    postFx: false,
  },
  high: {
    dpr: [1, 2],
    antialias: true,
    shadows: true,
    shadowMapSize: 2048,
    terrainSegments: 400,
    trees: 2600,
    clouds: 38,
    fogFar: 22000,
    postFx: true,
  },
};

export const QUALITY_COPY: Record<GraphicsQuality, string> = {
  low: 'No shadows or effects, fewer trees and clouds, lower resolution. For laptops and integrated graphics.',
  medium: 'Shadows, full scenery near the field, standard resolution. A good default.',
  high: 'Sharp shadows, the densest scenery, full resolution, plus bloom and vignette.',
};
