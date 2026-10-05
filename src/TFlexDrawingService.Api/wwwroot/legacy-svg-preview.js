const clampPreviewNumber = (value, min, max) => Math.min(max, Math.max(min, value));
const formatPreviewNumber = value => Number.isFinite(value) ? String(Math.round(value)) : "-";
const formatPreviewMetricValue = value => typeof value === "number" ? formatPreviewNumber(value) : escapeHtml(value);
const escapeHtml = value => String(value ?? "").replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll("\"", "&quot;").replaceAll("'", "&#39;");
const normalizePreviewToken = value => String(value || "").trim().toLowerCase().replaceAll(" ", "").replaceAll("-", "");
const hasValue = value => value !== null && value !== undefined;

const KII_ESCALATOR_PDF_BOUNDS = {
  minX: -11.34,
  minY: -67.02,
  maxX: 842.42,
  maxY: 339.94
};
const KII_ESCALATOR_PDF_PATHS = [
  "M-11.34 0.00 L144.85 0.00",
  "M144.85 0.00 L635.83 283.46",
  "M635.83 283.46 L842.41 283.46",
  "M842.41 283.46 L842.41 282.05",
  "M842.41 282.05 L831.08 282.05",
  "M831.08 282.05 L831.08 223.82",
  "M638.88 225.52 L831.08 225.52",
  "M831.08 223.82 L639.33 223.82",
  "M-11.34 0.00 L-11.34 -1.42",
  "M-11.34 -1.42 L0.00 -1.42",
  "M0.00 -1.42 L0.00 -67.01",
  "M34.02 -59.64 L148.36 -59.64",
  "M148.36 -59.64 L639.33 223.82",
  "M638.88 225.52 L147.90 -57.94",
  "M147.90 -57.94 L0.00 -57.94",
  "M0.00 -67.01 L34.02 -67.01",
  "M34.02 -67.01 L34.02 -59.64",
  "M56.81 56.47 L55.39 56.43 L53.97 56.31 L52.56 56.11 L51.17 55.83 L49.79 55.48 L48.44 55.04 L47.11 54.54 L45.81 53.96 L44.55 53.30 L43.32 52.58 L42.14 51.79 L41.01 50.94 L39.92 50.02 L38.89 49.04 L37.91 48.01 L36.99 46.92 L36.14 45.79 L35.35 44.61 L34.63 43.38 L33.97 42.12 L33.39 40.82 L32.89 39.49 L32.45 38.14 L32.10 36.76 L31.82 35.37 L31.62 33.96 L31.50 32.55 L31.46 31.12 L31.50 29.70 L31.62 28.29 L31.82 26.88 L32.10 25.49 L32.45 24.11 L32.89 22.75 L33.39 21.43 L33.97 20.13 L34.63 18.87 L35.35 17.64 L36.14 16.46 L36.99 15.32 L37.91 14.24 L38.89 13.21 L39.92 12.23 L41.01 11.31 L42.14 10.46 L43.32 9.67 L44.55 8.94 L45.81 8.29 L47.11 7.71 L48.44 7.20 L49.79 6.77 L51.17 6.42 L52.56 6.14 L53.97 5.94 L55.39 5.82 L56.81 5.78",
  "M56.81 56.47 L145.71 56.47",
  "M56.81 53.40 L55.35 53.36 L53.90 53.21 L52.46 52.98 L51.04 52.65 L49.64 52.22 L48.28 51.71 L46.95 51.11 L45.67 50.42 L44.43 49.65 L43.24 48.80 L42.12 47.88 L41.05 46.88 L40.06 45.81 L39.13 44.69 L38.28 43.50 L37.51 42.26 L36.82 40.98 L36.22 39.65 L35.71 38.29 L35.29 36.89 L34.95 35.47 L34.72 34.03 L34.57 32.58 L34.53 31.12 L34.57 29.67 L34.72 28.22 L34.95 26.78 L35.29 25.36 L35.71 23.96 L36.22 22.60 L36.82 21.27 L37.51 19.98 L38.28 18.75 L39.13 17.56 L40.06 16.43 L41.05 15.37 L42.12 14.37 L43.24 13.45 L44.43 12.60 L45.67 11.83 L46.95 11.14 L48.28 10.54 L49.64 10.03 L51.04 9.60 L52.46 9.27 L53.90 9.03 L55.35 8.89 L56.81 8.84",
  "M68.26 5.78 L56.81 5.78",
  "M68.26 17.01 L126.65 17.01",
  "M68.26 13.95 L127.47 13.95",
  "M712.64 300.47 L672.32 300.47",
  "M672.32 300.47 L668.41 300.41 L664.51 300.22 L660.62 299.90 L656.74 299.45 L652.88 298.88 L649.03 298.18 L645.22 297.35 L641.43 296.41 L637.67 295.33 L633.96 294.14 L630.28 292.82 L626.65 291.39 L623.06 289.84 L619.53 288.17 L612.65 284.48",
  "M712.64 297.41 L673.14 297.41",
  "M673.14 297.41 L669.23 297.35 L665.33 297.16 L661.44 296.84 L657.56 296.39 L653.70 295.82 L649.85 295.12 L646.04 294.29 L642.25 293.34 L638.49 292.27 L634.78 291.08 L631.10 289.76 L627.47 288.33 L623.88 286.77 L620.35 285.10 L613.47 281.42",
  "M56.81 53.40 L146.53 53.40",
  "M146.53 53.40 L149.55 53.46 L152.57 53.62 L155.59 53.89 L158.59 54.27 L161.58 54.75 L164.54 55.34 L167.49 56.04 L170.41 56.84 L173.29 57.74 L176.15 58.75 L178.96 59.86 L181.74 61.06 L184.47 62.37 L188.91 64.76",
  "M188.91 64.76 L632.52 320.88",
  "M631.70 323.94 L188.08 67.82",
  "M145.71 56.47 L148.73 56.52 L151.75 56.68 L154.77 56.95 L157.77 57.33 L160.76 57.81 L163.72 58.40 L166.67 59.10 L169.59 59.90 L172.47 60.80 L175.33 61.81 L178.14 62.92 L180.92 64.13 L183.65 65.43 L188.08 67.82",
  "M692.19 336.87 L688.28 336.81 L684.38 336.61 L680.49 336.29 L676.61 335.85 L672.75 335.28 L668.91 334.58 L665.09 333.75 L661.30 332.80 L657.55 331.73 L653.83 330.54 L650.15 329.22 L646.52 327.79 L642.94 326.23 L639.41 324.56 L632.52 320.88",
  "M691.37 339.93 L687.46 339.87 L683.56 339.68 L679.67 339.36 L675.79 338.91 L671.93 338.34 L668.09 337.64 L664.27 336.81 L660.48 335.86 L656.73 334.79 L653.01 333.60 L649.33 332.28 L645.70 330.85 L642.12 329.29 L638.59 327.62 L635.11 325.84 L631.70 323.94",
  "M613.47 281.42 L169.85 25.30",
  "M127.47 13.95 L130.50 14.00 L133.52 14.16 L136.53 14.43 L139.54 14.81 L142.52 15.29 L145.49 15.88 L148.43 16.58 L151.35 17.38 L154.24 18.28 L157.09 19.29 L159.91 20.40 L162.68 21.61 L165.41 22.91 L169.85 25.30",
  "M612.65 284.48 L169.03 28.36",
  "M126.65 17.01 L129.68 17.06 L132.70 17.22 L135.71 17.49 L138.72 17.87 L141.70 18.35 L144.67 18.94 L147.61 19.64 L150.53 20.44 L153.42 21.35 L156.27 22.35 L159.09 23.46 L161.86 24.67 L164.59 25.97 L169.03 28.36",
  "M724.10 292.31 L725.55 292.36 L727.00 292.50 L728.44 292.74 L729.86 293.07 L731.26 293.49 L732.62 294.00 L733.95 294.61 L735.24 295.29 L736.47 296.06 L737.66 296.91 L738.79 297.84 L739.85 298.83 L740.85 299.90 L741.77 301.03 L742.62 302.21 L743.39 303.45 L744.08 304.73 L744.68 306.06 L745.19 307.43 L745.62 308.82 L745.95 310.24 L746.19 311.68 L746.33 313.13 L746.38 314.59 L746.33 316.05 L746.19 317.50 L745.95 318.94 L745.62 320.36 L745.19 321.75 L744.68 323.12 L744.08 324.44 L743.39 325.73 L742.62 326.97 L741.77 328.15 L740.85 329.28 L739.85 330.34 L738.79 331.34 L737.66 332.27 L736.47 333.11 L735.24 333.88 L733.95 334.57 L732.62 335.17 L731.26 335.69 L729.86 336.11 L728.44 336.44 L727.00 336.68 L725.55 336.82 L724.10 336.87",
  "M724.10 336.87 L692.19 336.87",
  "M724.10 289.25 L725.52 289.29 L726.93 289.41 L728.34 289.61 L729.74 289.88 L731.11 290.24 L732.47 290.67 L733.79 291.18 L735.09 291.76 L736.35 292.41 L737.58 293.13 L738.76 293.92 L739.90 294.78 L740.98 295.69 L742.02 296.67 L742.99 297.70 L743.91 298.79 L744.76 299.92 L745.55 301.11 L746.28 302.33 L746.93 303.59 L747.51 304.89 L748.02 306.22 L748.45 307.57 L748.80 308.95 L749.08 310.34 L749.28 311.75 L749.40 313.17 L749.44 314.59 L749.40 316.01 L749.28 317.43 L749.08 318.83 L748.80 320.23 L748.45 321.60 L748.02 322.96 L747.51 324.29 L746.93 325.58 L746.28 326.85 L745.55 328.07 L744.76 329.25 L743.91 330.39 L742.99 331.48 L742.02 332.51 L740.98 333.48 L739.90 334.40 L738.76 335.26 L737.58 336.05 L736.35 336.77 L735.09 337.42 L733.79 338.00 L732.47 338.51 L731.11 338.94 L729.74 339.30 L728.34 339.57 L726.93 339.77 L725.52 339.89 L724.10 339.93",
  "M724.10 339.93 L691.37 339.93",
  "M724.10 289.25 L712.64 289.25",
  "M56.81 8.84 L68.26 8.84",
  "M712.64 292.31 L724.10 292.31",
  "M145.71 141.22 L172.00 69.04",
  "M146.53 138.16 L150.76 61.46",
  "M127.47 98.70 L132.00 22.02",
  "M691.37 220.59 L651.11 324.46",
  "M692.19 217.53 L684.33 328.66",
  "M831.08 270.71 L831.43 270.71",
  "M831.43 270.71 L831.57 270.72 L831.71 270.74 L831.84 270.79 L831.96 270.85 L832.08 270.93 L832.18 271.02 L832.27 271.12 L832.35 271.24 L832.41 271.36 L832.46 271.50 L832.48 271.63 L832.49 271.77",
  "M832.49 271.77 L832.49 279.21",
  "M833.91 280.63 L833.73 280.62 L833.54 280.58 L833.37 280.52 L833.20 280.44 L833.05 280.34 L832.91 280.21 L832.79 280.08 L832.68 279.92 L832.60 279.75 L832.54 279.58 L832.51 279.40 L832.49 279.21",
  "M833.91 280.63 L841.35 280.63",
  "M841.35 280.63 L841.49 280.64 L841.63 280.67 L841.76 280.71 L841.88 280.77 L842.00 280.85 L842.10 280.94 L842.19 281.05 L842.27 281.16 L842.33 281.29 L842.38 281.42 L842.41 281.55 L842.41 281.69",
  "M842.41 281.69 L842.41 282.05",
  "M842.41 282.05 L831.08 282.05",
  "M831.08 282.05 L831.08 270.71",
  "M-11.34 -1.42 L-11.34 -1.77",
  "M-11.34 -1.77 L-11.33 -1.91 L-11.30 -2.05 L-11.26 -2.18 L-11.20 -2.30 L-11.12 -2.42 L-11.03 -2.52 L-10.92 -2.61 L-10.81 -2.69 L-10.68 -2.75 L-10.55 -2.80 L-10.28 -2.83",
  "M-10.28 -2.83 L-2.83 -2.83",
  "M-1.42 -4.25 L-1.43 -4.07 L-1.47 -3.89 L-1.53 -3.71 L-1.61 -3.54 L-1.71 -3.39 L-1.83 -3.25 L-1.97 -3.13 L-2.13 -3.02 L-2.29 -2.94 L-2.47 -2.88 L-2.65 -2.85 L-2.83 -2.83",
  "M-1.42 -4.25 L-1.42 -11.69",
  "M-1.42 -11.69 L-1.41 -11.83 L-1.38 -11.97 L-1.34 -12.10 L-1.27 -12.22 L-1.20 -12.34 L-1.11 -12.44 L-1.00 -12.54 L-0.89 -12.61 L-0.76 -12.67 L-0.63 -12.72 L-0.49 -12.75 L-0.35 -12.76",
  "M-0.35 -12.76 L0.00 -12.76",
  "M0.00 -12.76 L0.00 -1.42",
  "M0.00 -1.42 L-11.34 -1.42",
  "M68.26 17.01 L68.26 0.00",
  "M68.26 0.00 L58.68 0.00",
  "M68.26 17.01 L60.25 16.94 L50.40 16.67",
  "M50.40 16.67 L50.18 16.65 L49.96 16.61 L49.75 16.54 L49.55 16.44 L49.36 16.32 L49.20 16.17 L49.05 16.00 L48.93 15.82 L48.83 15.62 L48.76 15.41 L48.71 15.19 L48.70 14.97",
  "M48.70 14.97 L48.70 13.88",
  "M48.70 13.88 L48.71 13.74 L48.74 13.59 L48.79 13.45 L48.85 13.32 L48.93 13.19 L49.03 13.08 L49.23 12.93",
  "M49.23 12.93 L50.89 11.92 L52.61 11.00 L54.37 10.17 L56.17 9.42 L58.00 8.76 L59.87 8.19 L61.75 7.72 L63.66 7.33 L65.59 7.04 L68.26 6.80",
  "M60.73 7.96 L58.68 0.00",
  "M56.34 9.35 L56.35 9.35",
  "M56.35 9.35 L56.19 9.34 L56.03 9.29 L55.88 9.21 L55.75 9.11 L55.65 8.98 L55.57 8.83 L55.52 8.67 L55.50 8.50",
  "M55.50 8.50 L55.50 6.24",
  "M55.50 6.24 L55.52 6.07 L55.57 5.91 L55.65 5.76 L55.75 5.63 L55.88 5.53 L56.03 5.45 L56.19 5.40 L56.35 5.39",
  "M56.35 5.39 L59.91 4.76",
  "M712.64 300.47 L712.64 283.46",
  "M712.64 283.46 L722.23 283.46",
  "M730.50 300.13 L722.50 300.37 L712.64 300.47",
  "M732.20 298.43 L732.19 298.65 L732.15 298.87 L732.07 299.08 L731.98 299.28 L731.85 299.47 L731.71 299.63 L731.54 299.78 L731.35 299.90 L731.15 300.00 L730.94 300.07 L730.72 300.12 L730.50 300.13",
  "M732.20 298.43 L732.20 297.35",
  "M731.68 296.39 L731.80 296.48 L731.90 296.58 L732.00 296.70 L732.07 296.82 L732.13 296.96 L732.18 297.10 L732.20 297.35",
  "M712.64 290.27 L714.59 290.43 L716.52 290.68 L718.43 291.03 L720.33 291.47 L722.20 292.00 L724.05 292.63 L725.86 293.34 L727.64 294.14 L729.37 295.03 L731.68 296.39",
  "M720.17 291.43 L722.23 283.46",
  "M724.57 292.82 L724.55 292.82",
  "M725.40 291.97 L725.38 292.13 L725.34 292.29 L725.26 292.44 L725.15 292.57 L725.02 292.68 L724.88 292.75 L724.72 292.80 L724.55 292.82",
  "M725.40 291.97 L725.40 289.70",
  "M724.55 288.85 L724.72 288.87 L724.88 288.92 L725.02 288.99 L725.15 289.10 L725.26 289.23 L725.34 289.38 L725.38 289.53 L725.40 289.70",
  "M724.55 288.85 L721.00 288.22"
];
const KII_ESCALATOR_REFERENCE = {
  rise: 3900,
  totalRun: 11633,
  lowerLanding: 2178,
  upperLanding: 2435,
  pitLength: 4253,
  pitDepth: 1110,
  pdfRise: 283.46,
  pdfRun: 842.41
};
const KII_ESCALATOR_EXCLUDED_PDF_PATHS = new Set([
  "M145.71 141.22 L172.00 69.04",
  "M146.53 138.16 L150.76 61.46",
  "M127.47 98.70 L132.00 22.02",
  "M691.37 220.59 L651.11 324.46",
  "M692.19 217.53 L684.33 328.66"
]);

export function renderLegacyShaftPreviewSvg(dimensions) {
  const svgWidth = 380;
  const svgHeight = 360;
  const paddingX = 75;
  const paddingY = 30;
  const rightReserve = 45;
  const bottomReserve = 100;
  const drawingWidth = svgWidth - paddingX - rightReserve;
  const drawingHeight = svgHeight - paddingY - bottomReserve;

  const shaftRect = { x: 0, y: 0, width: dimensions.ah, height: dimensions.bh };
  const shaftWallThickness = clampPreviewNumber(Math.min(dimensions.ah, dimensions.bh) * 0.075, 95, 160);
  const shaftConcreteOuterRect = {
    x: -shaftWallThickness,
    y: -shaftWallThickness,
    width: dimensions.ah + shaftWallThickness * 2,
    height: dimensions.bh + shaftWallThickness * 2
  };
  const cabinSideWallMm = 31;
  const cabinRearWallMm = dimensions.rearCwt
    ? (dimensions.entrances > 1 ? (dimensions.dk || 141) : (dimensions.bottomGap || 30))
    : 30;
  const cabinFrontWallMm = dimensions.rearCwt
    ? (dimensions.dk || 141)
    : (dimensions.kk || 45);
  const cabinInnerX = dimensions.carCenterX
    ? dimensions.carCenterX - dimensions.aa / 2
    : (dimensions.ah - dimensions.aa) / 2;
  const cabinOuterWidth = Math.max(dimensions.as || 0, dimensions.aa + cabinSideWallMm * 2);
  const cabinOuterHeight = dimensions.rearCwt && dimensions.bs
    ? dimensions.bs
    : dimensions.bb + cabinRearWallMm + cabinFrontWallMm;
  const cabinSideWall = (cabinOuterWidth - dimensions.aa) / 2;
  const doorWidthMm = dimensions.doorWidth || dimensions.aa * 0.55;
  const doorDepthMm = dimensions.lehy?.carDoorDepth ?? (dimensions.xizi?.cdt ?? (dimensions.centerOpeningDoor ? 60 : 96));
  const doorSpacingMm = 30;
  const carDoorFrontGap = dimensions.centerOpeningDoor ? 130 : 151;
  const cabinOuterTopY = dimensions.rearCwt && dimensions.carCenterY
    ? dimensions.carCenterY - dimensions.bb / 2 - cabinRearWallMm
    : dimensions.bh - carDoorFrontGap - doorDepthMm - cabinOuterHeight;
  const baseCabinOuterRect = dimensions.xizi
    ? { x: dimensions.xizi.cabin.x - cabinSideWallMm, y: dimensions.xizi.cabin.y - 30, width: dimensions.xizi.cabin.width + cabinSideWallMm * 2, height: dimensions.xizi.cabin.height + 60 }
    : dimensions.lehy ? dimensions.lehy.cabinOuterRect : {
    x: cabinInnerX - cabinSideWall,
    y: cabinOuterTopY,
    width: cabinOuterWidth,
    height: cabinOuterHeight
  };
  const baseCabinInnerRect = dimensions.xizi ? { ...dimensions.xizi.cabin } : dimensions.lehy ? dimensions.lehy.cabinInnerRect : {
    x: baseCabinOuterRect.x + cabinSideWall,
    y: baseCabinOuterRect.y + cabinRearWallMm,
    width: dimensions.aa,
    height: dimensions.bb
  };
  const doorX = dimensions.centerOpeningDoor
    ? baseCabinInnerRect.x + baseCabinInnerRect.width / 2 + (dimensions.a4 || 0) - doorWidthMm / 2
    : dimensions.rearCwt && dimensions.rearDoorDirection === "right"
      ? baseCabinOuterRect.x - 25
      : baseCabinOuterRect.x + baseCabinOuterRect.width + 25 - doorWidthMm;
  const makeDoorPair = side => {
    const isRear = side === "rear";
    const carDoorY = dimensions.lehyPro
      ? (isRear
        ? baseCabinOuterRect.y
        : baseCabinOuterRect.y + baseCabinOuterRect.height - doorDepthMm)
      : (isRear
        ? baseCabinOuterRect.y - doorDepthMm
        : baseCabinOuterRect.y + baseCabinOuterRect.height);
    const landingDoorY = isRear
      ? carDoorY - doorSpacingMm - doorDepthMm
      : carDoorY + doorDepthMm + doorSpacingMm;

    return {
      side,
      carDoor: {
        x: doorX,
        y: carDoorY,
        width: doorWidthMm,
        height: doorDepthMm
      },
      landingDoor: {
        x: doorX,
        y: landingDoorY,
        width: doorWidthMm,
        height: doorDepthMm
      }
    };
  };
  const baseDoorPairs = dimensions.xizi
    ? dimensions.xizi.doors.map((door, index) => ({
      side: index === 0 ? "front" : "rear",
      carDoor: dimensions.xizi.carDoors[index],
      landingDoor: door
    }))
    : dimensions.lehy ? dimensions.lehy.doorPairs : [makeDoorPair("front")];
  if (!dimensions.xizi && !dimensions.lehy && dimensions.entrances > 1) {
    baseDoorPairs.push(makeDoorPair("rear"));
  }

  const buildBaseCwtRect = () => {
    if (!dimensions.ww || !dimensions.wg) return null;

    const gap = 30;
    if (dimensions.rearCwt) {
      const width = dimensions.wg;
      const height = dimensions.ww;
      const centerX = dimensions.carCenterX || (baseCabinInnerRect.x + baseCabinInnerRect.width / 2);
      return {
        x: centerX - width / 2,
        y: Number.isFinite(dimensions.cwtY) ? dimensions.cwtY : gap,
        width,
        height
      };
    }

    const width = dimensions.ww;
    const height = dimensions.wg;
    const measuredX = Number.isFinite(dimensions.cwtX) && dimensions.cwtX + width <= baseCabinOuterRect.x + 20
      ? dimensions.cwtX
      : baseCabinOuterRect.x - width - 48;
    const measuredY = Number.isFinite(dimensions.cwtY)
      ? dimensions.cwtY
      : (dimensions.bh - height) / 2;

    return {
      x: clampPreviewNumber(measuredX, gap, Math.max(gap, baseCabinOuterRect.x - width - 22)),
      y: clampPreviewNumber(measuredY, gap, Math.max(gap, dimensions.bh - height - gap)),
      width,
      height
    };
  };

  const baseCwtRect = dimensions.xizi ? dimensions.xizi.cwtRect : dimensions.lehy ? dimensions.lehy.cwtRect : buildBaseCwtRect();
  const mirrorRect = rect => dimensions.mirrorX && !dimensions.rearCwt && !dimensions.lehy
    ? { ...rect, x: dimensions.ah - rect.x - rect.width }
    : rect;
  const mirrorDoorPair = pair => ({
    ...pair,
    carDoor: mirrorRect(pair.carDoor),
    landingDoor: mirrorRect(pair.landingDoor)
  });
  const cabinOuterRect = mirrorRect(baseCabinOuterRect);
  const cabinInnerRect = mirrorRect(baseCabinInnerRect);
  const doorPairs = baseDoorPairs.map(mirrorDoorPair);
  const cwtRect = baseCwtRect ? mirrorRect(baseCwtRect) : null;
  const doorRects = doorPairs.flatMap(pair => [pair.carDoor, pair.landingDoor]);
  const rects = [shaftConcreteOuterRect, shaftRect, cabinOuterRect, cabinInnerRect, ...doorRects, ...(cwtRect ? [cwtRect] : [])];
  const margin = 140;
  const bounds = rects.reduce((acc, rect) => ({
    minX: Math.min(acc.minX, rect.x),
    minY: Math.min(acc.minY, rect.y),
    maxX: Math.max(acc.maxX, rect.x + rect.width),
    maxY: Math.max(acc.maxY, rect.y + rect.height)
  }), { minX: 0, minY: 0, maxX: dimensions.ah, maxY: dimensions.bh });
  bounds.minX -= margin;
  bounds.minY -= margin;
  bounds.maxX += margin;
  bounds.maxY += margin;

  const scale = Math.min(drawingWidth / (bounds.maxX - bounds.minX), drawingHeight / (bounds.maxY - bounds.minY));
  const mapX = value => paddingX + (value - bounds.minX) * scale;
  const mapY = value => paddingY + (value - bounds.minY) * scale;
  const mapSize = value => value * scale;
  const rectAttrs = rect =>
    `x="${mapX(rect.x).toFixed(1)}" y="${mapY(rect.y).toFixed(1)}" width="${mapSize(rect.width).toFixed(1)}" height="${mapSize(rect.height).toFixed(1)}"`;
  const lineAttrs = (x1, y1, x2, y2) =>
    `x1="${mapX(x1).toFixed(1)}" y1="${mapY(y1).toFixed(1)}" x2="${mapX(x2).toFixed(1)}" y2="${mapY(y2).toFixed(1)}"`;
  const pathPoint = (x, y) => `${mapX(x).toFixed(1)} ${mapY(y).toFixed(1)}`;
  const rectPath = rect => [
    `M ${pathPoint(rect.x, rect.y)}`,
    `L ${pathPoint(rect.x + rect.width, rect.y)}`,
    `L ${pathPoint(rect.x + rect.width, rect.y + rect.height)}`,
    `L ${pathPoint(rect.x, rect.y + rect.height)}`,
    "Z"
  ].join(" ");
  const pillRadius = Math.max(2, Math.min(9, mapSize(doorDepthMm / 2))).toFixed(1);
  const isInside = (inner, outer) =>
    inner.x >= outer.x
    && inner.y >= outer.y
    && inner.x + inner.width <= outer.x + outer.width
    && inner.y + inner.height <= outer.y + outer.height;
  const intersects = (first, second) =>
    first.x < second.x + second.width
    && first.x + first.width > second.x
    && first.y < second.y + second.height
    && first.y + first.height > second.y;
  const cabinCollision = !isInside(cabinOuterRect, shaftRect) || (cwtRect && intersects(cabinOuterRect, cwtRect));
  const cwtCollision = cwtRect && (!isInside(cwtRect, shaftRect) || intersects(cabinOuterRect, cwtRect));
  const getCabinDoorOpeningBounds = pair => {
    if (dimensions.xizi) return { start: dimensions.xizi.doorAxisX - dimensions.xizi.doorWidth / 2, end: dimensions.xizi.doorAxisX + dimensions.xizi.doorWidth / 2 };
    const openingWidth = dimensions.jj || pair.carDoor.width;
    let start;
    let end;

    if (dimensions.centerOpeningDoor) {
      const center = pair.carDoor.x + pair.carDoor.width / 2;
      start = center - openingWidth / 2;
      end = center + openingWidth / 2;
    } else if (dimensions.rearCwt && dimensions.rearDoorDirection === "right") {
      start = cabinInnerRect.x + 25;
      end = start + openingWidth;
    } else if (dimensions.mirrorX) {
      start = cabinInnerRect.x + 25;
      end = start + openingWidth;
    } else {
      end = cabinInnerRect.x + cabinInnerRect.width - 25;
      start = end - openingWidth;
    }

    const outerLeft = cabinOuterRect.x;
    const outerRight = cabinOuterRect.x + cabinOuterRect.width;
    return {
      start: clampPreviewNumber(start, outerLeft, outerRight),
      end: clampPreviewNumber(end, outerLeft, outerRight)
    };
  };
  const shaftLeft = shaftRect.x;
  const shaftRight = shaftRect.x + shaftRect.width;
  const shaftTop = shaftRect.y;
  const shaftBottom = shaftRect.y + shaftRect.height;
  const getShaftDoorOpeningBounds = pair => {
    if (dimensions.xizi) return { start: pair.landingDoor.x, end: pair.landingDoor.x + pair.landingDoor.width };
    const cabinOpening = getCabinDoorOpeningBounds(pair);
    const start = clampPreviewNumber(cabinOpening.start - 100, shaftLeft, shaftRight);
    const end = clampPreviewNumber(cabinOpening.end + 100, shaftLeft, shaftRight);
    return {
      start: Math.min(start, end),
      end: Math.max(start, end)
    };
  };
  const frontOpening = getShaftDoorOpeningBounds(doorPairs[0]);
  const rearOpening = doorPairs[1] ? getShaftDoorOpeningBounds(doorPairs[1]) : null;
  const cabinDoorOpening = getCabinDoorOpeningBounds(doorPairs[0]);
  const concreteCollisionRects = [
    {
      x: shaftLeft - shaftWallThickness,
      y: shaftTop - shaftWallThickness,
      width: shaftWallThickness,
      height: dimensions.bh + shaftWallThickness * 2
    },
    {
      x: shaftRight,
      y: shaftTop - shaftWallThickness,
      width: shaftWallThickness,
      height: dimensions.bh + shaftWallThickness * 2
    },
    {
      x: shaftLeft - shaftWallThickness,
      y: shaftBottom,
      width: frontOpening.start - shaftLeft + shaftWallThickness,
      height: shaftWallThickness
    },
    {
      x: frontOpening.end,
      y: shaftBottom,
      width: shaftRight - frontOpening.end + shaftWallThickness,
      height: shaftWallThickness
    },
    ...(rearOpening
      ? [
          {
            x: shaftLeft - shaftWallThickness,
            y: shaftTop - shaftWallThickness,
            width: rearOpening.start - shaftLeft + shaftWallThickness,
            height: shaftWallThickness
          },
          {
            x: rearOpening.end,
            y: shaftTop - shaftWallThickness,
            width: shaftRight - rearOpening.end + shaftWallThickness,
            height: shaftWallThickness
          }
        ]
      : [{
          x: shaftLeft - shaftWallThickness,
          y: shaftTop - shaftWallThickness,
          width: dimensions.ah + shaftWallThickness * 2,
          height: shaftWallThickness
        }])
  ].filter(rect => rect.width > 1 && rect.height > 1);
  const intersectsWithTolerance = (first, second, tolerance = 2) =>
    first.x < second.x + second.width - tolerance
    && first.x + first.width > second.x + tolerance
    && first.y < second.y + second.height - tolerance
    && first.y + first.height > second.y + tolerance;
  const isDoorWallCollision = pair =>
    [pair.carDoor, pair.landingDoor].some(door =>
      concreteCollisionRects.some(wall => intersectsWithTolerance(door, wall)));
  const concreteCutouts = [
    shaftRect,
    { x: frontOpening.start, y: shaftBottom, width: frontOpening.end - frontOpening.start, height: shaftWallThickness },
    ...(rearOpening
      ? [{ x: rearOpening.start, y: -shaftWallThickness, width: rearOpening.end - rearOpening.start, height: shaftWallThickness }]
      : [])
  ];
  const concretePath = [
    rectPath(shaftConcreteOuterRect),
    ...concreteCutouts.filter(rect => rect.width > 1 && rect.height > 1).map(rectPath)
  ].join(" ");
  const wallPath = [
    `M ${pathPoint(shaftLeft, shaftBottom)} L ${pathPoint(shaftLeft, shaftTop)}`,
    rearOpening
      ? `M ${pathPoint(shaftLeft, shaftTop)} L ${pathPoint(rearOpening.start, shaftTop)} M ${pathPoint(rearOpening.end, shaftTop)} L ${pathPoint(shaftRight, shaftTop)}`
      : `M ${pathPoint(shaftLeft, shaftTop)} L ${pathPoint(shaftRight, shaftTop)}`,
    `M ${pathPoint(shaftRight, shaftTop)} L ${pathPoint(shaftRight, shaftBottom)}`,
    `M ${pathPoint(shaftLeft, shaftBottom)} L ${pathPoint(frontOpening.start, shaftBottom)}`,
    `M ${pathPoint(frontOpening.end, shaftBottom)} L ${pathPoint(shaftRight, shaftBottom)}`
  ].join(" ");
  const cwtMarkup = cwtRect
    ? `<rect class="shaft-preview-svg__counterweight ${cwtCollision ? "shaft-preview-svg__counterweight--collision" : ""}" ${rectAttrs(cwtRect)} rx="3" />`
    : "";
  const doorMarkup = doorPairs.map(pair => {
    const doorCollisionClass = isDoorWallCollision(pair) ? "shaft-preview-svg__door--collision" : "";
    return `
      <rect class="shaft-preview-svg__landing-door ${doorCollisionClass}" ${rectAttrs(pair.landingDoor)} rx="${pillRadius}" />
      <rect class="shaft-preview-svg__car-door ${doorCollisionClass}" ${rectAttrs(pair.carDoor)} rx="${pillRadius}" />
    `;
  }).join("");
  const shoulderMarkup = doorPairs.map(pair => {
    const y = pair.side === "rear"
      ? cabinOuterRect.y
      : cabinOuterRect.y + cabinOuterRect.height;
    const outerLeft = cabinOuterRect.x;
    const outerRight = cabinOuterRect.x + cabinOuterRect.width;
    const opening = getCabinDoorOpeningBounds(pair);
    const minSegment = 20;
    const segments = [];

    if (opening.start - outerLeft > minSegment) {
      segments.push(`<line class="shaft-preview-svg__car-shoulder" ${lineAttrs(outerLeft, y, opening.start, y)} />`);
    }
    if (outerRight - opening.end > minSegment) {
      segments.push(`<line class="shaft-preview-svg__car-shoulder" ${lineAttrs(opening.end, y, outerRight, y)} />`);
    }

    return segments.join("");
  }).join("");
  const xiziDirectionMarkup = dimensions.xizi ? doorPairs.map(pair => {
    const center = dimensions.xizi.doorAxisX;
    const y = dimensions.xizi.cabin.y + dimensions.xizi.cabin.height + 145;
    const direction = dimensions.xizi.hand === "LEFT" ? -1 : 1;
    const points = dimensions.xizi.hand === "CENTR"
      ? [[center - 65, y + 25], [center, y], [center + 65, y + 25]]
      : [[center, y + 25], [center + direction * 65, y]];
    return `<path class="shaft-preview-svg__door-opening-marker" d="M ${pathPoint(...points[0])} L ${pathPoint(...points[1])}${points[2] ? ` M ${pathPoint(...points[1])} L ${pathPoint(...points[2])}` : ""}" />`;
  }).join("") : "";
  const openingMarkerMarkup = doorPairs.map(pair => {
    const isRear = pair.side === "rear";
    const opening = getCabinDoorOpeningBounds(pair);
    const outerY = isRear ? cabinOuterRect.y : cabinOuterRect.y + cabinOuterRect.height;
    const innerY = isRear ? cabinInnerRect.y : cabinInnerRect.y + cabinInnerRect.height;

    return `
      <line class="shaft-preview-svg__door-opening-marker" ${lineAttrs(opening.start, outerY, opening.start, innerY)} />
      <line class="shaft-preview-svg__door-opening-marker" ${lineAttrs(opening.end, outerY, opening.end, innerY)} />
    `;
  }).join("");
  const outerSvg = {
    left: mapX(shaftConcreteOuterRect.x), right: mapX(shaftConcreteOuterRect.x + shaftConcreteOuterRect.width),
    top: mapY(shaftConcreteOuterRect.y), bottom: mapY(shaftConcreteOuterRect.y + shaftConcreteOuterRect.height)
  };
  const widthStart = mapX(cabinInnerRect.x), widthEnd = mapX(cabinInnerRect.x + cabinInnerRect.width);
  const widthY = outerSvg.top - 18;
  const depthX = outerSvg.right + 22;
  const depthStart = mapY(cabinInnerRect.y), depthEnd = mapY(cabinInnerRect.y + cabinInnerRect.height);
  const doorStart = mapX(cabinDoorOpening.start), doorEnd = mapX(cabinDoorOpening.end);
  const doorY = outerSvg.bottom + 18;
  const overallWidthY = outerSvg.bottom + 54;
  const overallDepthX = outerSvg.left - 54;
  const widthLabel = dimensions.xizi ? "CW" : "AA";
  const depthLabel = dimensions.xizi ? "CD" : "BB";
  const doorLabel = dimensions.xizi ? "OP" : "JJ";
  const clearWidthText = `${doorLabel} ${formatPreviewNumber(cabinDoorOpening.end - cabinDoorOpening.start)}${dimensions.xizi ? ` · HL6 ${formatPreviewNumber(dimensions.xizi.doorAxisX)}` : ""}`;
  const overallWidthLabel = dimensions.xizi ? "HW" : "AH";
  const overallDepthLabel = dimensions.xizi ? "HD" : "BH";
  const horizontalLine = (x1, x2, y, name = "") => `<line${name ? ` data-dimension="${name}"` : ""} x1="${x1.toFixed(1)}" y1="${y.toFixed(1)}" x2="${x2.toFixed(1)}" y2="${y.toFixed(1)}"/>`;
  const verticalLine = (x, y1, y2, name = "") => `<line${name ? ` data-dimension="${name}"` : ""} x1="${x.toFixed(1)}" y1="${y1.toFixed(1)}" x2="${x.toFixed(1)}" y2="${y2.toFixed(1)}"/>`;
  const dimensionLabel = (name, x, y, text, vertical = false) =>
    `<text data-dimension-label="${name}" x="${x.toFixed(1)}" y="${y.toFixed(1)}"${vertical ? ` transform="rotate(-90 ${x.toFixed(1)} ${y.toFixed(1)})"` : ""}>${text}</text>`;
  const tick = (x, y, vertical = true) => vertical
    ? `<line x1="${x.toFixed(1)}" y1="${(y - 4).toFixed(1)}" x2="${x.toFixed(1)}" y2="${(y + 4).toFixed(1)}"/>`
    : `<line x1="${(x - 4).toFixed(1)}" y1="${y.toFixed(1)}" x2="${(x + 4).toFixed(1)}" y2="${y.toFixed(1)}"/>`;
  const shaftDimensions = `
    <g fill="none" stroke="#64748b" stroke-width="1">
      ${horizontalLine(widthStart, widthEnd, widthY, "cabin-width")}
      ${verticalLine(widthStart, outerSvg.top, widthY)}
      ${verticalLine(widthEnd, outerSvg.top, widthY)}
      ${tick(widthStart, widthY)}${tick(widthEnd, widthY)}
      ${verticalLine(depthX, depthStart, depthEnd, "cabin-depth")}
      ${horizontalLine(outerSvg.right, depthX, depthStart)}
      ${horizontalLine(outerSvg.right, depthX, depthEnd)}
      ${tick(depthX, depthStart, false)}${tick(depthX, depthEnd, false)}
      ${horizontalLine(doorStart, doorEnd, doorY, "door-clear-width")}
      ${verticalLine(doorStart, outerSvg.bottom, doorY)}
      ${verticalLine(doorEnd, outerSvg.bottom, doorY)}
      ${tick(doorStart, doorY)}${tick(doorEnd, doorY)}
      ${horizontalLine(mapX(shaftLeft), mapX(shaftRight), overallWidthY, "shaft-width")}
      ${verticalLine(mapX(shaftLeft), outerSvg.bottom, overallWidthY)}
      ${verticalLine(mapX(shaftRight), outerSvg.bottom, overallWidthY)}
      ${tick(mapX(shaftLeft), overallWidthY)}${tick(mapX(shaftRight), overallWidthY)}
      ${verticalLine(overallDepthX, mapY(shaftTop), mapY(shaftBottom), "shaft-depth")}
      ${horizontalLine(overallDepthX, outerSvg.left, mapY(shaftTop))}
      ${horizontalLine(overallDepthX, outerSvg.left, mapY(shaftBottom))}
      ${tick(overallDepthX, mapY(shaftTop), false)}${tick(overallDepthX, mapY(shaftBottom), false)}
    </g>
    <g fill="#465360" font-size="11" font-weight="700" text-anchor="middle">
      ${dimensionLabel("cabin-width", (widthStart + widthEnd) / 2, widthY - 5, `${widthLabel} ${formatPreviewNumber(cabinInnerRect.width)}`)}
      ${dimensionLabel("door-clear-width", (doorStart + doorEnd) / 2, doorY + 13, clearWidthText)}
      ${dimensionLabel("shaft-width", (mapX(shaftLeft) + mapX(shaftRight)) / 2, overallWidthY + 13, `${overallWidthLabel} ${formatPreviewNumber(dimensions.ah)}`)}
      ${dimensionLabel("cabin-depth", depthX + 9, (depthStart + depthEnd) / 2, `${depthLabel} ${formatPreviewNumber(cabinInnerRect.height)}`, true)}
      ${dimensionLabel("shaft-depth", overallDepthX - 8, (mapY(shaftTop) + mapY(shaftBottom)) / 2, `${overallDepthLabel} ${formatPreviewNumber(dimensions.bh)}`, true)}
    </g>`;

  return `
    <svg class="shaft-preview-svg" viewBox="0 0 ${svgWidth} ${svgHeight}" role="img" aria-label="План шахты">
      <defs>
        <pattern id="shaftConcreteHatch" width="8" height="8" patternUnits="userSpaceOnUse" patternTransform="rotate(30)">
          <rect class="shaft-preview-svg__shaft-concrete-fill" width="8" height="8" />
          <line class="shaft-preview-svg__shaft-concrete-hatch" x1="0" y1="0" x2="0" y2="8" />
        </pattern>
      </defs>
      <rect class="shaft-preview-svg__shaft-fill" ${rectAttrs(shaftRect)} />
      <path class="shaft-preview-svg__shaft-concrete" d="${concretePath}" fill-rule="evenodd" />
      <path class="shaft-preview-svg__shaft" d="${wallPath}" />
      <line class="shaft-preview-svg__axis" x1="${mapX(shaftRect.x + shaftRect.width / 2).toFixed(1)}" y1="${mapY(shaftRect.y).toFixed(1)}" x2="${mapX(shaftRect.x + shaftRect.width / 2).toFixed(1)}" y2="${mapY(shaftRect.y + shaftRect.height).toFixed(1)}" />
      <line class="shaft-preview-svg__axis" x1="${mapX(shaftRect.x).toFixed(1)}" y1="${mapY(shaftRect.y + shaftRect.height / 2).toFixed(1)}" x2="${mapX(shaftRect.x + shaftRect.width).toFixed(1)}" y2="${mapY(shaftRect.y + shaftRect.height / 2).toFixed(1)}" />
      ${cwtMarkup}
      <rect class="shaft-preview-svg__car-outer ${cabinCollision ? "shaft-preview-svg__car--collision" : ""}" ${rectAttrs(cabinOuterRect)} rx="6" />
      <rect class="shaft-preview-svg__car-inner ${cabinCollision ? "shaft-preview-svg__car--collision" : ""}" ${rectAttrs(cabinInnerRect)} rx="5" />
      ${shoulderMarkup}
      ${openingMarkerMarkup}
      ${xiziDirectionMarkup}
      ${doorMarkup}
      ${shaftDimensions}
    </svg>`;
}



export function renderLegacyShaftPreviewMetrics(dimensions) {
  if (dimensions.xizi) {
    return [
      ["HW", dimensions.xizi.width, "Ширина шахты"], ["HD", dimensions.xizi.depth, "Глубина шахты"],
      ["CW", dimensions.xizi.cabinWidth, "Ширина кабины"], ["CD", dimensions.xizi.cabinDepth, "Глубина кабины"],
      ["HW1", dimensions.xizi.cabinAxisX, "Ось кабины от левой стены"],
      ["HL6", dimensions.xizi.doorAxisX, "Ось дверного проема от левой стены"],
      ["DOP", dimensions.xizi.doorOffset, "Смещение дверного проема"],
      ["OP", dimensions.xizi.doorWidth, "Ширина дверного проема"],
      ["NBENT", dimensions.xizi.entrances, "Число входов"], ["$HAND", dimensions.xizi.hand, "Открывание дверей"],
      ["$CWTLOC", ({ "12": "Сзади", "13": "Слева", "24": "Справа" })[dimensions.xizi.place] || dimensions.xizi.place, "Расположение противовеса"]
    ].map(([name, value, label]) => `
      <div class="shaft-preview__metric"><dt>${escapeHtml(name)}</dt><dd>${formatPreviewMetricValue(value)}<span>${escapeHtml(label)}</span></dd></div>`).join("");
  }
  const metrics = [
    ["AH", dimensions.ah, "Ширина шахты"],
    ["BH", dimensions.bh, "Глубина шахты"],
    ["AA", dimensions.aa, "Ширина кабины"],
    ["BB", dimensions.bb, "Глубина кабины"],
    ["JJ", dimensions.jj, "Ширина дверей"],
    ...(dimensions.doorWidth && dimensions.doorWidth !== dimensions.jj
      ? [[dimensions.doorWidthMetricName, dimensions.doorWidth, dimensions.centerOpeningDoor ? "Расчетная ширина CO" : "Расчетная ширина 2S"]]
      : []),
    [
      dimensions.rearCwt ? "DK" : "KK",
      dimensions.rearCwt ? dimensions.dk : dimensions.kk,
      dimensions.rearCwt
        ? "От дальней стенки двери кабины до внутренней стенки кабины"
        : "Передняя стенка кабины"
    ],
    [dimensions.rearCwt ? "CJ" : "A4", dimensions.a4, "Эксцентриситет"],
    ["Тип", dimensions.rearCwt ? "Задний" : "Боковой", "Компоновка противовеса"],
    ...(dimensions.rearCwt && !dimensions.centerOpeningDoor
      ? [["Открывание", dimensions.rearDoorDirectionLabel, "Направление ТО-дверей"]]
      : dimensions.rearCwt
        ? []
        : [["Место", dimensions.cwtPlaceLabel, "Положение противовеса"]]),
    ...(dimensions.rearCwt && dimensions.cb
      ? [["CB", dimensions.cb, "От оси кабины до правой стены"]]
      : []),
    ...(dimensions.rearCwt && dimensions.carCenterY
      ? [["CC", dimensions.carCenterY, "От задней стены до оси кабины"]]
      : []),
    [dimensions.cwtMetricName, dimensions.cwtMetricValue, "Противовес по ширине"],
    [dimensions.cwtDepthLabel, dimensions.cwtDepth, "Противовес по глубине"],
    ...(dimensions.rearCwt && dimensions.cwtRearClearance !== null
      ? [["Зазор", dimensions.cwtRearClearance, "От задней стены до противовеса"]]
      : [])
  ];

  return metrics
    .filter(([, value]) => value !== null && value !== undefined && value !== "")
    .map(([name, value, label]) => `
      <div class="shaft-preview__metric">
        <dt>${escapeHtml(name)}</dt>
        <dd>${formatPreviewMetricValue(value)}<span>${escapeHtml(label)}</span></dd>
      </div>`)
    .join("");
}



export function renderLegacyEscalatorPreviewSvg(dimensions) {
  const svgWidth = 420;
  const svgHeight = 300;
  const paddingX = 18;
  const paddingY = 30;
  const totalRun = dimensions.totalRun || KII_ESCALATOR_REFERENCE.totalRun;
  const rise = dimensions.rise || KII_ESCALATOR_REFERENCE.rise;
  const lowerLanding = dimensions.lowerLanding || KII_ESCALATOR_REFERENCE.lowerLanding;
  const upperLanding = dimensions.upperLanding || KII_ESCALATOR_REFERENCE.upperLanding;
  const inclineRun = dimensions.inclineRun || Math.max(totalRun - lowerLanding - upperLanding, rise);
  const inclineEnd = lowerLanding + inclineRun;
  const pitLength = dimensions.pitLength || KII_ESCALATOR_REFERENCE.pitLength;
  const pitDepth = dimensions.pitDepth || KII_ESCALATOR_REFERENCE.pitDepth;
  const pitToken = normalizePreviewToken(dimensions.pitType);
  const hasPit = pitToken.includes("приям") || pitToken.includes("pit");
  const floorThickness = clampPreviewNumber(rise * 0.045, 150, 260);
  const pitWallThickness = clampPreviewNumber(floorThickness * 0.82, 130, 220);
  const slabOverhang = clampPreviewNumber(totalRun * 0.06, 600, 1100);
  const lowerRightSlabWidth = slabOverhang * 1.35;
  const lowerOpeningEnd = Math.max(pitLength, lowerLanding + floorThickness * 2);
  const pdfToMmX = totalRun / KII_ESCALATOR_REFERENCE.pdfRun;
  const pdfToMmY = rise / KII_ESCALATOR_REFERENCE.pdfRise;
  const upperTrussRightX = 831.08 * pdfToMmX;
  const upperOpeningStart = clampPreviewNumber(
    lowerLanding + inclineRun * 0.45,
    lowerLanding + floorThickness * 2,
    Math.max(lowerLanding + floorThickness * 2, inclineEnd - upperLanding * 0.25)
  );
  const upperOpeningEnd = upperTrussRightX + floorThickness * 0.08;
  const pdfBoundsMm = {
    minX: KII_ESCALATOR_PDF_BOUNDS.minX * pdfToMmX,
    minY: KII_ESCALATOR_PDF_BOUNDS.minY * pdfToMmY,
    maxX: KII_ESCALATOR_PDF_BOUNDS.maxX * pdfToMmX,
    maxY: KII_ESCALATOR_PDF_BOUNDS.maxY * pdfToMmY
  };
  const constructionBounds = {
    minX: -slabOverhang,
    minY: -Math.max(pitDepth + pitWallThickness, floorThickness),
    maxX: Math.max(totalRun + slabOverhang, upperOpeningEnd + slabOverhang),
    maxY: Math.max(rise + floorThickness, pdfBoundsMm.maxY)
  };
  const bounds = {
    minX: Math.min(pdfBoundsMm.minX, constructionBounds.minX),
    minY: Math.min(pdfBoundsMm.minY, constructionBounds.minY),
    maxX: Math.max(pdfBoundsMm.maxX, constructionBounds.maxX),
    maxY: Math.max(pdfBoundsMm.maxY, constructionBounds.maxY)
  };
  const boundsWidth = bounds.maxX - bounds.minX;
  const boundsHeight = bounds.maxY - bounds.minY;
  const scale = Math.min(
    (svgWidth - paddingX * 2) / boundsWidth,
    (svgHeight - paddingY * 2) / boundsHeight
  );
  const translateX = paddingX - bounds.minX * scale;
  const translateY = paddingY + bounds.maxY * scale;
  const mapX = value => translateX + value * scale;
  const mapY = value => translateY - value * scale;
  const dimensionLine = (x1, y1, x2, y2, label, labelX, labelY, anchor = "middle", transform = "") =>
    `<line x1="${x1.toFixed(1)}" y1="${y1.toFixed(1)}" x2="${x2.toFixed(1)}" y2="${y2.toFixed(1)}" stroke="#334155" stroke-width="1.2"/><text data-dimension="${label.split(" ")[0]}" x="${labelX.toFixed(1)}" y="${labelY.toFixed(1)}"${transform ? ` transform="${transform}"` : ""} text-anchor="${anchor}" fill="#334155" font-size="11" font-weight="700" paint-order="stroke" stroke="white" stroke-width="3" stroke-linejoin="round">${label}</text>`;
  const x0 = mapX(0), xRun = mapX(totalRun), y0 = mapY(0), yRise = mapY(rise);
  const escalatorDimensions = [
    dimensionLine(x0 - 12, yRise, x0 - 12, y0, `HE ${formatPreviewNumber(rise)}`, x0 + 5, (yRise + y0) / 2, "middle", `rotate(-90 ${(x0 + 5).toFixed(1)} ${((yRise + y0) / 2).toFixed(1)})`),
    dimensionLine(x0, y0 + 12, xRun, y0 + 12, `TG ${formatPreviewNumber(totalRun)}`, (x0 + xRun) / 2, y0 + 9),
    dimensionLine(x0, y0 + 29, mapX(lowerLanding), y0 + 29, `TK ${formatPreviewNumber(lowerLanding)}`, (x0 + mapX(lowerLanding)) / 2, y0 + 26),
    dimensionLine(mapX(totalRun - upperLanding), yRise - 12, xRun, yRise - 12, `TJ ${formatPreviewNumber(upperLanding)}`, (mapX(totalRun - upperLanding) + xRun) / 2, yRise - 15)
  ].join("");
  const slabRects = [
    ...(!hasPit ? [
      { x: -slabOverhang, y: -floorThickness, width: slabOverhang, height: floorThickness },
      { x: lowerOpeningEnd, y: -floorThickness, width: lowerRightSlabWidth, height: floorThickness }
    ] : []),
    { x: -slabOverhang, y: rise - floorThickness, width: upperOpeningStart + slabOverhang, height: floorThickness },
    { x: upperOpeningEnd, y: rise - floorThickness, width: slabOverhang, height: floorThickness }
  ].filter(rect => rect.width > 8 && rect.height > 8);
  const slabMarkup = slabRects
    .map(rect => `
      <rect class="escalator-preview-svg__slab" x="${rect.x.toFixed(1)}" y="${rect.y.toFixed(1)}" width="${rect.width.toFixed(1)}" height="${rect.height.toFixed(1)}" />`)
    .join("");
  const pitWallMarkup = hasPit
    ? `<path class="escalator-preview-svg__pit-wall" d="
        M ${(-slabOverhang).toFixed(1)} 0
        L 0 0
        L 0 ${(-pitDepth).toFixed(1)}
        L ${lowerOpeningEnd.toFixed(1)} ${(-pitDepth).toFixed(1)}
        L ${lowerOpeningEnd.toFixed(1)} 0
        L ${(lowerOpeningEnd + lowerRightSlabWidth).toFixed(1)} 0
        L ${(lowerOpeningEnd + lowerRightSlabWidth).toFixed(1)} ${(-floorThickness).toFixed(1)}
        L ${(lowerOpeningEnd + pitWallThickness).toFixed(1)} ${(-floorThickness).toFixed(1)}
        L ${(lowerOpeningEnd + pitWallThickness).toFixed(1)} ${(-pitDepth - pitWallThickness).toFixed(1)}
        L ${(-pitWallThickness).toFixed(1)} ${(-pitDepth - pitWallThickness).toFixed(1)}
        L ${(-pitWallThickness).toFixed(1)} ${(-floorThickness).toFixed(1)}
        L ${(-slabOverhang).toFixed(1)} ${(-floorThickness).toFixed(1)}
        Z" />`
    : "";
  const lowerOpeningMarkup = hasPit
    ? ""
    : `<path class="escalator-preview-svg__opening-edge" d="M 0 0 L 0 ${(-pitDepth).toFixed(1)} L ${lowerOpeningEnd.toFixed(1)} ${(-pitDepth).toFixed(1)} L ${lowerOpeningEnd.toFixed(1)} 0" />`;
  const upperOpeningMarkup = `
    <path class="escalator-preview-svg__opening-edge" d="
      M ${upperOpeningStart.toFixed(1)} ${rise.toFixed(1)} L ${upperOpeningStart.toFixed(1)} ${(rise - floorThickness).toFixed(1)}
      M ${upperOpeningEnd.toFixed(1)} ${rise.toFixed(1)} L ${upperOpeningEnd.toFixed(1)} ${(rise - floorThickness).toFixed(1)}" />`;
  const paths = KII_ESCALATOR_PDF_PATHS
    .filter(path => !KII_ESCALATOR_EXCLUDED_PDF_PATHS.has(path))
    .map((path) => `<path class="escalator-preview-svg__pdf-line" d="${path}" />`)
    .join("");

  return `
    <svg class="shaft-preview-svg escalator-preview-svg" viewBox="0 0 ${svgWidth} ${svgHeight}" role="img" aria-label="Профиль эскалатора">
      <defs>
        <pattern id="escalatorConcreteHatch" width="9" height="9" patternUnits="userSpaceOnUse" patternTransform="rotate(28)">
          <rect class="escalator-preview-svg__concrete-fill" x="0" y="0" width="9" height="9" />
          <line class="escalator-preview-svg__slab-hatch" x1="0" y1="0" x2="0" y2="9" />
        </pattern>
      </defs>
      <g transform="translate(${translateX.toFixed(2)} ${translateY.toFixed(2)}) scale(${scale.toFixed(5)} ${(-scale).toFixed(5)})">
        ${slabMarkup}
        ${pitWallMarkup}
        ${lowerOpeningMarkup}
        ${upperOpeningMarkup}
        <g transform="scale(${pdfToMmX.toFixed(5)} ${pdfToMmY.toFixed(5)})">
          ${paths}
        </g>
      </g>
      <g aria-label="Размеры эскалатора">${escalatorDimensions}</g>
    </svg>`;
}

function renderEscalatorHatch(rect, className = "escalator-preview-svg__slab-hatch") {
  const spacing = 180;
  const lines = [];
  const start = rect.x - rect.height;
  const end = rect.x + rect.width;

  for (let x = start; x < end; x += spacing) {
    const x1 = clampPreviewNumber(x, rect.x, rect.x + rect.width);
    const x2 = clampPreviewNumber(x + rect.height, rect.x, rect.x + rect.width);
    const y1 = rect.y + (x1 - x);
    const y2 = rect.y + rect.height - (x + rect.height - x2);
    lines.push(`<line class="${className}" x1="${x1.toFixed(1)}" y1="${y1.toFixed(1)}" x2="${x2.toFixed(1)}" y2="${y2.toFixed(1)}" />`);
  }

  return lines.join("");
}



export function renderLegacyEscalatorPreviewMetrics(dimensions) {
  const supportCount = [
    dimensions.support1Visible,
    dimensions.support2Visible
  ].filter(Boolean).length;
  const metrics = [
    ["HE", dimensions.rise, "Высота подъема"],
    ["alpha", `${formatPreviewNumber(dimensions.angle)}°`, "Угол наклона"],
    ["TG", dimensions.totalRun, "Горизонтальная длина"],
    ["LL", dimensions.overallLength, "Габаритная длина"],
    ["W", dimensions.stepWidth, "Ширина ступеней"],
    ["N", dimensions.escalatorCount, "Количество эскалаторов"],
    ["Wpit", dimensions.pitWidth, "Ширина проема"],
    ["TK", dimensions.lowerLanding, "Нижняя площадка"],
    ["TJ", dimensions.upperLanding, "Верхняя площадка"],
    ["Lpit", dimensions.pitLength, "Длина приямка"],
    ["Dpit", dimensions.pitDepth, "Глубина приямка"],
    ["SUP", supportCount > 0 ? supportCount : "Нет", "Промежуточные опоры"],
    ["V", hasValue(dimensions.speed) ? `${dimensions.speed} м/с` : null, "Скорость"],
    ["Режим", dimensions.mode, "Режим работы"],
    ["Балюстрада", dimensions.balustrade, "Материал"],
    ["Исполнение", dimensions.pitType, "Приямок"]
  ];

  return metrics
    .filter(([, value]) => value !== null && value !== undefined && value !== "")
    .map(([name, value, label]) => `
      <div class="shaft-preview__metric">
        <dt>${escapeHtml(name)}</dt>
        <dd>${formatPreviewMetricValue(value)}<span>${escapeHtml(label)}</span></dd>
      </div>`)
    .join("");
}
