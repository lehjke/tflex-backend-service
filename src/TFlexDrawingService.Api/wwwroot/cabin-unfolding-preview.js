const finite = value => {
  if (value === null || value === undefined || String(value).trim() === "") return null;
  const number = Number(String(value).replace(",", "."));
  return Number.isFinite(number) ? number : null;
};
const str = value => String(value ?? "");
const bounded = (value, max = 54) => {
  const label = str(value);
  return label.length <= max ? label : `${label.slice(0, max - 1)}…`;
};
const xml = value => str(value).replace(/[&<>"']/gu, ch => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&apos;" }[ch]));
const CEILING_100 = ["ZCL-GN07", "ZCL-SS07", "ZCL-SS10", "ZCL-CN01", "ZCL-GS24"];

export function getCabinUnfoldingGeometry(context) {
  const num = key => finite(context?.[key]);
  const AA = num("AA"), BB = num("BB"), HL = num("HL"), HH = num("HH"), JJ = num("JJ");
  const A4 = finite(context?.A4 ?? context?.A4_v);
  const opening = str(context?.$Opening ?? context?.Opening), doorsType = str(context?.$DoorsType ?? context?.DoorsType);
  if ([AA, BB, HL, HH, JJ, A4].some(v => v === null) || [AA, BB, HL, HH, JJ].some(v => v <= 0)
    || HH > HL || JJ > AA || !["2S", "CO"].includes(opening) || !["1D-1G", "1D-2G"].includes(doorsType)
    || (opening === "CO" && Math.abs(A4) > 150)) return null;
  const cop = str(context?.$COP ?? context?.COP), copPlace = str(context?.$CopPlace ?? context?.CopPlace ?? context?.$CopPlace_v_1 ?? context?.CopPlace_v_1);
  const copWidth = num("WB"), ceilingType = str(context?.$CeilingType ?? context?.CeilingType);
  const ceilingHeight = num("CeilingHeight") ?? (CEILING_100.includes(ceilingType) ? 100 : 200);
  if (!copWidth || !["Front", "Side"].includes(copPlace) || ceilingHeight <= 0) return null;
  const through = doorsType === "1D-2G", doorCenter = AA / 2 + A4;
  const frontSegments = [doorCenter - JJ / 2, JJ, AA - doorCenter - JJ / 2];
  if (frontSegments.some(v => v < 0)) return null;
  const positive = values => values.filter(v => v !== null && v > 0);
  const rearSegments = positive([num("B"), num("B"), num("A")]);
  const leftSegments = positive([num("D1"), num("D2"), num("C1"), num("C2")]);
  const planSideSegments = positive([num("L"), num("M"), num("J"), copWidth + 1, num("G")]);
  // The plan's COP strip is WB+1; the elevation strip is WB+15.
  // Two adjacent panel allowances are 7 mm each (one panel takes both if J=0).
  const sideSegments = positive([num("L"), num("M") === null ? null : num("M") - (num("J") > 0 ? 7 : 14),
    num("J") > 0 ? num("J") - 7 : null, copWidth + 15, num("G")]);
  const validChain = (parts, length) => parts.length > 0 && Math.abs(parts.reduce((a, b) => a + b, 0) - length) < .01;
  if (!validChain(rearSegments, AA) || !validChain(leftSegments, BB)
    || !validChain(planSideSegments, BB) || !validChain(sideSegments, BB)) return null;
  const planCopCenter = copPlace === "Front" ? frontSegments[0] / 2 : BB - num("G") - (copWidth + 1) / 2;
  // Native CO elevations are centred; A4 is shown in the top plan only.
  const elevationFrontSegments = opening === "CO" ? [(AA - JJ) / 2, JJ, (AA - JJ) / 2] : [...frontSegments].reverse();
  const copCenter = copPlace === "Front" ? AA - elevationFrontSegments[2] / 2 : BB - num("G") - (copWidth + 15) / 2;
  const titaniumCode = str(context?.$TitanimCoatedCode ?? context?.TitanimCoatedCode);
  const decorCode = str(context?.$DecorCode ?? context?.DecorCode);
  return { AA, BB, HL, HH, JJ, A4, opening, doorsType, through, doorCenter,
    frontDoor: { wall: "D", center: doorCenter, width: JJ }, rearDoor: through ? { wall: "A", center: doorCenter, width: JJ } : null,
    cop, copPlace, copWall: copPlace === "Front" ? "D" : "B", copWidth, copCenter, planCopCenter,
    rearSegments, leftSegments, sideSegments, planSideSegments, frontSegments, elevationFrontSegments,
    ceilingType, ceilingHeight, floorType: str(context?.$FloorType ?? context?.FloorType), decorCode,
    colorCode: decorCode === "Painted steel" ? str(context?.$ColorCode ?? context?.ColorCode) : "", titaniumCode,
    floorProtrusion: num("KK") ?? (JJ > 1200 || HH > 2600 ? 80 : 55) };
}

export function renderCabinUnfoldingPreviewSvg(g, { planOnly = false } = {}) {
  if (!g) return "";
  const { AA, BB, HL, HH, JJ, A4, opening, through, cop, copPlace, copWidth, ceilingHeight } = g;
  const f = value => Number(value.toFixed(2));
  const line = (x1, y1, x2, y2, attrs = "") => `<line x1="${f(x1)}" y1="${f(y1)}" x2="${f(x2)}" y2="${f(y2)}" ${attrs}/>`;
  const text = (x, y, label, attrs = "") => `<text x="${f(x)}" y="${f(y)}" ${attrs}>${xml(label)}</text>`;
  const dimension = (id, x, y, parts, scale, vertical = false, witness = 0) => {
    let offset = 0, svg = `<g data-panel-chain="${id}" fill="none" stroke="#525866" stroke-width=".65">`;
    const tick = pos => vertical ? line(x - 3, pos + 3, x + 3, pos - 3) : line(pos - 3, y + 3, pos + 3, y - 3);
    for (let i = 0; i < parts.length; i++) {
      const size = parts[i] * scale, start = (vertical ? y : x) + offset, end = start + size;
      svg += vertical ? line(x, start, x, end) : line(start, y, end, y);
      svg += tick(start) + tick(end);
      svg += vertical ? line(witness, start, x + (x > witness ? 4 : -4), start) : line(start, witness, start, y + (y > witness ? 4 : -4));
      if (i === parts.length - 1) svg += vertical ? line(witness, end, x + (x > witness ? 4 : -4), end) : line(end, witness, end, y + (y > witness ? 4 : -4));
      const cx = vertical ? x - 5 : start + size / 2, cy = vertical ? start + size / 2 : y - 5;
      // Narrow end panels use a leader so their numbers remain readable.
      if (!vertical && size < 22) {
        const lx = i === 0 ? start - 14 : end + 14;
        svg += line(start + size / 2, y, lx, y - 15) + text(lx, y - 19, f(parts[i]), `data-panel-size="${id}" stroke="none"`);
      } else svg += text(cx, cy, f(parts[i]), `data-panel-size="${id}" stroke="none"${vertical ? ` transform="rotate(-90 ${f(cx)} ${f(cy)})"` : ""}`);
      offset += size;
    }
    return `${svg}</g>`;
  };
  const ps = Math.min(240 / AA, 300 / BB), pw = AA * ps, pd = BB * ps;
  const px = (480 - pw) / 2, py = through ? 130 : 110;
  const planBottom = py + pd;
  let svg = `<svg class="shaft-preview-svg cabin-unfolding-preview-svg" viewBox="0 0 480 ${planOnly ? planBottom + 115 : 1480}" role="img" aria-label="Развертка кабины LEHY, план панелей и четыре вида">`;
  svg += text(240, 25, "План кабины · размеры панелей, мм", 'class="caption"');
  const planA = through ? g.frontSegments : g.rearSegments;
  const planB = copPlace === "Side" ? g.planSideSegments : g.leftSegments;
  const planC = through && copPlace === "Side" ? [...planB].reverse() : g.leftSegments;
  const doorPlan = (y, upward) => {
    const left = px + g.frontSegments[0] * ps, right = left + JJ * ps, sign = upward ? -1 : 1;
    return line(px, y, left, y) + line(right, y, px + pw, y)
      + line(left, y, left, y + sign * 8) + line(right, y, right, y + sign * 8);
  };
  svg += `<g data-cabin-plan="true" fill="none" stroke="#202632" stroke-width="1.5">`;
  svg += through ? doorPlan(py, true) : line(px, py, px + pw, py);
  svg += line(px, py, px, planBottom) + line(px + pw, py, px + pw, planBottom) + doorPlan(planBottom, false);
  const seams = (id, parts, vertical, x, y) => {
    let offset = 0, s = "";
    for (const part of parts.slice(0, -1)) { offset += part * ps; s += vertical
      ? line(x - 4, y + offset, x + 4, y + offset, `data-plan-seam="${id}"`)
      : line(x + offset, y - 4, x + offset, y + 4, `data-plan-seam="${id}"`); }
    return s;
  };
  if (!through) svg += seams("A", planA, false, px, py);
  svg += seams("B", planB, true, px + pw, py) + seams("C", planC, true, px, py);
  const strip = (wall, center) => wall === "D"
    ? `<rect data-plan-cop="D" x="${f(px + (center - (copWidth + 1) / 2) * ps)}" y="${f(planBottom - 2)}" width="${f((copWidth + 1) * ps)}" height="4" fill="white"/>`
    : `<rect data-plan-cop="${wall}" x="${f(wall === "B" ? px + pw - 2 : px - 2)}" y="${f(py + (center - (copWidth + 1) / 2) * ps)}" width="4" height="${f((copWidth + 1) * ps)}" fill="white"/>`;
  svg += strip(g.copWall, g.planCopCenter);
  if (through && copPlace === "Side") svg += strip("C", BB - g.planCopCenter);
  svg += line(px + pw / 2, py - 10, px + pw / 2, planBottom + 10, 'stroke-width=".6" stroke-dasharray="10 4 2 4"');
  const cx = px + pw / 2, cy = py + pd / 2;
  for (const [id, dx, dy] of [["A", 0, -1], ["B", 1, 0], ["C", -1, 0], ["D", 0, 1]]) {
    const x = cx + dx * 24, y = cy + dy * 24;
    svg += `<circle cx="${f(x)}" cy="${f(y)}" r="12" fill="white" stroke-width=".65"/>` + text(x, y + 5, id);
  }
  svg += `</g>`;
  svg += dimension("plan-A", px, py - 22, planA, ps, false, py - 5);
  svg += dimension("plan-AA", px, py - 49, [AA], ps, false, py - 5);
  svg += dimension("plan-B", px + pw + 25, py, planB, ps, true, px + pw + 5);
  svg += dimension("plan-C", px - 25, py, planC, ps, true, px - 5);
  svg += dimension("plan-BB", px - 55, py, [BB], ps, true, px - 5);
  svg += dimension("plan-D", px, planBottom + 37, g.frontSegments, ps, false, planBottom + 10);
  if (copPlace === "Front") svg += dimension("plan-COP-center", px, planBottom + 65, [g.planCopCenter], ps, false, planBottom + 10);
  svg += text(20, planBottom + 90, `Проём ${opening} · JJ ${JJ} · A4 ${f(A4)} · КОП ${cop}`, 'class="note"');
  if (planOnly) return `${svg}</svg>`;
  const miniScale = Math.min(150 / AA, 165 / BB), mw = AA * miniScale, md = BB * miniScale;
  const miniY = planBottom + 145;
  for (const [kind, center] of [["roof", 125], ["floor", 355]]) {
    const x = center - mw / 2, y = miniY;
    svg += text(center, y - 20, kind === "roof" ? "Потолок" : "Напольное покрытие", 'class="caption"');
    svg += `<g fill="none" stroke="#202632" stroke-width="1">`;
    if (kind === "roof") {
      svg += `<rect x="${f(x)}" y="${f(y)}" width="${f(mw)}" height="${f(md)}"/>`;
      for (const [ix, iy] of [[30, 20], [45, 35], [57, 47]]) svg += `<rect x="${f(x + ix * miniScale)}" y="${f(y + iy * miniScale)}" width="${f(mw - 2 * ix * miniScale)}" height="${f(md - 2 * iy * miniScale)}"/>`;
    } else {
      const left = x + g.frontSegments[0] * miniScale, right = left + JJ * miniScale, k = g.floorProtrusion * miniScale;
      svg += `<path d="M${f(x)},${f(y)}${through ? ` H${f(left)} V${f(y - k)} H${f(right)} V${f(y)}` : ""} H${f(x + mw)} V${f(y + md)} H${f(right)} V${f(y + md + k)} H${f(left)} V${f(y + md)} H${f(x)} Z"/>`;
    }
    svg += `</g>` + dimension(`${kind}-width`, x, y + md + 28, [AA], miniScale, false, y + md + 8);
    svg += text(center, y + md + 56, kind === "roof" ? g.ceilingType : g.floorType, 'font-size="12"');
  }
  const es = Math.min(160 / Math.max(AA, BB), 210 / (HL + ceilingHeight + 30));
  const height = (HL + ceilingHeight + 30) * es, row1 = miniY + md + 115, row2 = row1 + height + 105;
  const wall = (id, center, y, widthMm, parts, door = false, copHere = false) => {
    const width = widthMm * es, x = center - width / 2, top = y + ceilingHeight * es, floor = y + (HL + ceilingHeight) * es;
    let s = text(center, y - 15, `Вид ${id} · ${{ A: "задняя", B: "правая", C: "левая", D: "фронтальная" }[id]}`, 'class="caption"');
    s += `<g fill="none" stroke="#202632" stroke-width="1.2"><rect x="${f(x)}" y="${f(y)}" width="${f(width)}" height="${f(height)}"/>`;
    s += line(x, top, x + width, top) + line(x, floor, x + width, floor);
    let off = 0;
    for (const part of parts.slice(0, -1)) { off += part; s += line(x + off * es, top, x + off * es, floor - 75 * es, `data-wall-seam="${id}"`); }
    if (door) {
      const left = x + parts[0] * es, dw = JJ * es, dy = floor - HH * es;
      s += `<rect data-door-wall="${id}" x="${f(left)}" y="${f(dy)}" width="${f(dw)}" height="${f(HH * es)}" fill="white"/>`;
      s += line(left + dw / 2, dy, left + dw / 2, floor, 'stroke-width=".6"');
      s += line(x, floor - 75 * es, left, floor - 75 * es) + line(left + dw, floor - 75 * es, x + width, floor - 75 * es);
    } else s += line(x, floor - 75 * es, x + width, floor - 75 * es);
    if (copHere) {
      const c = x + g.copCenter * es, stripW = (copWidth + 15) * es;
      s += `<rect data-wall-cop="${id}" x="${f(c - stripW / 2)}" y="${f(top)}" width="${f(stripW)}" height="${f((HL - 75) * es)}" fill="white"/>`;
      // ponytail: detailed controls for native ND10; other COP models retain their real strip width and position.
      if (cop === "ZCB-ND10") {
        s += `<rect x="${f(c - 38.5 * es)}" y="${f(floor - 2080 * es)}" width="${f(77 * es)}" height="${f(182.5 * es)}"/>`;
        s += `<rect x="${f(c - stripW / 2)}" y="${f(floor - 1725 * es)}" width="${f(stripW)}" height="${f(25 * es)}"/>`;
        for (const [dx, h] of [[-65, 900], [0, 900], [65, 900], [0, 1071], [0, 1028]]) s += `<circle cx="${f(c + dx * es)}" cy="${f(floor - h * es)}" r="${f(17.5 * es)}"/>`;
      }
    }
    s += `</g>` + dimension(`wall-${id}`, x, floor + 27, parts, es, false, floor + 5);
    s += dimension(`wall-${id}-total`, x, floor + 56, [widthMm], es, false, floor + 5);
    s += dimension(`wall-${id}-HL`, x + width + 21, top, [HL], es, true, x + width + 4);
    if (door) s += text(center, floor + 81, `JJ ${JJ} · HH ${HH}`);
    return s;
  };
  svg += wall("A", 117, row1, AA, through ? g.elevationFrontSegments : g.rearSegments, through, through && copPlace === "Front");
  svg += wall("B", 352, row1, BB, copPlace === "Side" ? g.sideSegments : g.leftSegments, false, copPlace === "Side");
  svg += wall("C", 117, row2, BB, through && copPlace === "Side" ? g.sideSegments : g.leftSegments, false, through && copPlace === "Side");
  svg += wall("D", 352, row2, AA, g.elevationFrontSegments, true, copPlace === "Front");
  const bottom = row2 + height + 115;
  svg += text(20, bottom, `AA ${AA} · BB ${BB} · HL ${HL} · пояс ${ceilingHeight} · цоколь 75 · основание 30 мм`, 'class="note"');
  svg += text(20, bottom + 22, bounded(`Отделка ${g.decorCode}${g.colorCode ? ` · ${g.colorCode}` : ""}`), 'class="note"');
  return `${svg.replace('480 1480', `480 ${f(bottom + 45)}`)}</svg>`;
}
