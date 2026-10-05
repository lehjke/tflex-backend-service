import { getXiziPreviewGeometry, isXiziPreviewTemplate } from "./xizi-preview.js?v=20261004-restored-svg-1";
import { getLehyPreviewGeometry, isLehyPreviewTemplate } from "./lehy-preview.js?v=20261004-geometry-2";
import { getEscalatorPreviewGeometry } from "./escalator-preview.js?v=20261004-geometry-2";
import { getCabinUnfoldingGeometry, renderCabinUnfoldingPreviewSvg } from "./cabin-unfolding-preview.js?v=20261004-geometry-1";
import {
  renderLegacyEscalatorPreviewMetrics,
  renderLegacyEscalatorPreviewSvg,
  renderLegacyShaftPreviewMetrics,
  renderLegacyShaftPreviewSvg
} from "./legacy-svg-preview.js?v=20261004-restored-svg-1";

const finite = value => {
  if (value === null || value === undefined || String(value).trim() === "") return null;
  const parsed = Number(String(value).replace(",", "."));
  return Number.isFinite(parsed) ? parsed : null;
};

function lehyDimensions(template, context, geometry) {
  const rearCwt = geometry.rearCwt;
  const opening = context?.$s_1 ?? context?.s_1;
  return {
    lehy: geometry,
    ah: geometry.width,
    bh: geometry.depth,
    aa: geometry.cabinWidth,
    bb: geometry.cabinDepth,
    as: geometry.cabinOuterRect.width,
    jj: finite(context?.JJ),
    kk: finite(context?.KK),
    dk: finite(context?.DK),
    bs: finite(context?.BS),
    bottomGap: finite(context?.BB_CLEARANCE),
    doorWidth: geometry.doorWidth,
    doorWidthMetricName: geometry.centerOpening ? "2xJJ" : "1.5*JJ+175",
    centerOpeningDoor: geometry.centerOpening,
    entrances: geometry.entrances,
    cwtPlaceLabel: rearCwt ? "Сзади" : "Сбоку",
    cwtLayout: rearCwt ? "rear" : "side",
    mirrorX: geometry.mirrorX,
    rearDoorDirection: /^(направо|right|1)$/i.test(String(opening ?? "")) ? "right" : "left",
    rearDoorDirectionLabel: /^(направо|right|1)$/i.test(String(opening ?? "")) ? "Направо" : "Налево",
    a4: finite(rearCwt ? context?.CJ : context?.A4),
    carCenterX: geometry.cabinAxisX,
    carCenterY: rearCwt && finite(context?.CC) !== -1 ? finite(context?.CC) : null,
    cb: finite(context?.CB),
    cwtMetricName: rearCwt ? "A8" : template.id === "lehy_pro_side_cwt" ? "CA" : "A1",
    cwtMetricValue: finite(rearCwt ? context?.A8 : template.id === "lehy_pro_side_cwt" ? context?.CA : context?.A1) === -1
      ? null : finite(rearCwt ? context?.A8 : template.id === "lehy_pro_side_cwt" ? context?.CA : context?.A1),
    cwtDepth: rearCwt ? geometry.counterweightWidth : geometry.counterweightDepth,
    cwtDepthLabel: rearCwt ? "WW" : "WG",
    cwtRearClearance: rearCwt ? geometry.cwtRect?.y ?? null : null,
    lehyPro: !template.id.startsWith("lehy_l_pro_"),
    rearCwt,
    ww: geometry.counterweightWidth,
    wg: geometry.counterweightDepth
  };
}

function xiziDimensions(geometry) {
  return {
    xizi: geometry,
    ah: geometry.width,
    bh: geometry.depth,
    aa: geometry.cabinWidth,
    bb: geometry.cabinDepth,
    centerOpeningDoor: geometry.centerOpening,
    entrances: geometry.entrances,
    mirrorX: false,
    rearCwt: false,
    cwtLayout: "side",
    cwtPlaceLabel: ({ "12": "Сзади", "13": "Слева", "24": "Справа" })[geometry.place] || geometry.place,
    doorWidth: geometry.doorWidth,
    jj: geometry.doorWidth,
    kk: null,
    a4: geometry.doorOffset,
    cwtX: geometry.cwtRect?.x ?? null,
    cwtDepth: geometry.cwtRect?.height ?? null,
    cwtY: geometry.cwtRect?.y ?? null,
    cwtMetricName: "Положение",
    cwtMetricValue: null,
    cwtDepthLabel: "Противовес"
  };
}

function escalatorDimensions(geometry, context) {
  if (!geometry) return null;
  const supportVisible = code => geometry.supports.some(support => support.code === code);
  return {
    rise: geometry.HE,
    angle: geometry.alpha,
    lowerLanding: geometry.TK,
    upperLanding: geometry.TJ,
    inclineRun: geometry.run,
    totalRun: geometry.TG,
    overallLength: geometry.LL,
    stepWidth: finite(context?.W),
    speed: finite(context?.V ?? context?.V_v),
    escalatorCount: geometry.N,
    pitLength: geometry.Lpit,
    pitDepth: geometry.Dpit,
    trussDepth: finite(context?.D),
    balustradeHeight: geometry.CH,
    pitWidth: geometry.Wpit === -1 ? null : geometry.Wpit,
    mountingGap: finite(context?.HGAP),
    support1: geometry.supports.find(support => support.code === "SUP1")?.x,
    support2: geometry.supports.find(support => support.code === "SUP2")?.x,
    support1Visible: supportVisible("SUP1"),
    support2Visible: supportVisible("SUP2"),
    mode: context?.$Mode ?? "Нормальный",
    pitType: geometry.pitType,
    balustrade: geometry.balustradeMaterial
  };
}

function cabinMetrics(geometry) {
  if (!geometry) return "";
  const rows = [
    ["AA", geometry.AA, "Ширина кабины"], ["BB", geometry.BB, "Глубина кабины"],
    ["HL", geometry.HL, "Высота кабины"], ["HH", geometry.HH, "Высота двери"],
    ["JJ", geometry.JJ, "Ширина дверного проема"], ["A4", geometry.A4, "Смещение проема"]
  ];
  return rows.map(([name, value, label]) => `<div class="shaft-preview__metric"><dt>${name}</dt><dd>${value}<span>${label}</span></dd></div>`).join("");
}

export function renderLiveSvgPreview(template, context) {
  if (!template || !context) return "";
  if (isXiziPreviewTemplate(template)) {
    const geometry = getXiziPreviewGeometry(template, context);
    return geometry ? renderLegacyShaftPreviewSvg(xiziDimensions(geometry)) : "";
  }
  if (isLehyPreviewTemplate(template)) {
    const geometry = getLehyPreviewGeometry(template, context);
    return geometry ? renderLegacyShaftPreviewSvg(lehyDimensions(template, context, geometry)) : "";
  }
  if (template.id === "k_ii_type") {
    const geometry = getEscalatorPreviewGeometry(context);
    const dimensions = escalatorDimensions(geometry, context);
    return dimensions ? renderLegacyEscalatorPreviewSvg(dimensions) : "";
  }
  if (template.id === "razvertki_lehy") {
    return renderCabinUnfoldingPreviewSvg(getCabinUnfoldingGeometry(context), { planOnly: true });
  }
  return "";
}

export function renderLiveSvgPreviewMetrics(template, context) {
  if (!template || !context) return "";
  if (isXiziPreviewTemplate(template)) {
    const geometry = getXiziPreviewGeometry(template, context);
    return geometry ? renderLegacyShaftPreviewMetrics(xiziDimensions(geometry)) : "";
  }
  if (isLehyPreviewTemplate(template)) {
    const geometry = getLehyPreviewGeometry(template, context);
    return geometry ? renderLegacyShaftPreviewMetrics(lehyDimensions(template, context, geometry)) : "";
  }
  if (template.id === "k_ii_type") {
    const dimensions = escalatorDimensions(getEscalatorPreviewGeometry(context), context);
    return dimensions ? renderLegacyEscalatorPreviewMetrics(dimensions) : "";
  }
  if (template.id === "razvertki_lehy") return cabinMetrics(getCabinUnfoldingGeometry(context));
  return "";
}
