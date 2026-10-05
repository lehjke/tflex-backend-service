export function fitScale(pageWidth, scrollWidth, viewerWidth, padding = 24) {
  const availableWidth = scrollWidth || viewerWidth;
  return Math.max(0.1, (availableWidth - padding) / pageWidth);
}

export function clampZoom(scale) {
  return Math.min(4, Math.max(0.2, scale));
}

export function cropRect(width, height, fractions) {
  if (!fractions) return { x: 0, y: 0, width, height };
  const [left, top, cropWidth, cropHeight] = fractions;
  const x = Math.min(width, Math.max(0, left * width));
  const y = Math.min(height, Math.max(0, top * height));
  return {
    x,
    y,
    width: Math.min(width - x, Math.max(0, cropWidth * width)),
    height: Math.min(height - y, Math.max(0, cropHeight * height))
  };
}

export function cropTransform(crop, dpr) {
  return [dpr, 0, 0, dpr, -crop.x * dpr, -crop.y * dpr];
}

export const INITIAL_CROPS = Object.freeze({
  un_victor_mrl: [0.06, 0.12, 0.47, 0.61],
  un_victor_mrl_t: [0.06, 0.12, 0.47, 0.61],
  un_victor_r: [0.06, 0.12, 0.47, 0.61],
  lehy_l_pro_320_1050: [0.075, 0.12, 0.40, 0.62],
  lehy_l_pro_1050_2500: [0.075, 0.12, 0.40, 0.62],
  lehy_pro_side_cwt: [0.07, 0.12, 0.40, 0.60],
  lehy_pro_rear_cwt: [0.07, 0.12, 0.40, 0.60],
  k_ii_type: [0.12, 0.10, 0.78, 0.62],
  razvertki_lehy: [0.11, 0.11, 0.19, 0.31]
});
