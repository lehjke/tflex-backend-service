export function xiziArdCode(capacity, speed) {
  if (Number(capacity) <= 1050) return Number(speed) <= 1.75 ? "ARD_15" : Number(speed) <= 2 ? "ARD_22" : "ARD_37";
  return Number(speed) <= 1.75 ? "ARD_22_2" : "ARD_37_2";
}

export function isXiziManualOption(code, series, capacity, speed) {
  if (/^(?:CONTAINER_|40HQ$|20GP$|AC(?:COLD|HEAT)|AC$|RCC$)/i.test(code)) return false;
  if (code === "CWT_SIDE" && series !== "UN-Victor R") return false;
  return !code.startsWith("ARD_") || code === xiziArdCode(capacity, speed);
}

export function xiziConfigurationInput(template, series, capacity, speed, values, through, doorType, cwtSafety) {
  const car = template?.parameters?.find(p => p.name === "$CARTYPE_MENU")?.allowedValues?.find(label => {
    const match = label.match(/\/\s*(\d+)\s*\/\s*(\d+)×(\d+)$/);
    return match && Number(match[1]) === Number(capacity) && Number(match[2]) === values.AA && Number(match[3]) === values.BB;
  });
  if (!car) return null;
  return {
    $CARTYPE_MENU: car, $V: Number(speed).toFixed(Number(speed) === 1.75 ? 2 : 1),
    $N: String(values.stops), $R: String(values.TR / 1000),
    CH: values.HL, K: values.OH, S: values.PD, HW: values.AH, WTW: values.BH, OP: values.JJ, OPH: values.HH,
    NBENT_MENU: through ? 2 : 1, $DOOR_MENU: doorType === "CO" ? "CLD" : "TLD", $CWT: cwtSafety ? "WSAFE" : "WOSAF"
  };
}
