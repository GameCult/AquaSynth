// Flattens a Faust DSP JSON `ui` tree (factory.json) into the controls a slider panel needs.
// Shared by the browser app and the node test so both read the DSP's own UI declaration.
export function controlsOf(json) {
  const out = [];
  const walk = (items, group) => {
    for (const it of items ?? []) {
      if (it.items) walk(it.items, it.label ? [...group, it.label] : group);
      else if (/slider|nentry|checkbox|button/.test(it.type))
        out.push({
          address: it.address, label: it.label, group: group.join(" / "), type: it.type,
          init: Number(it.init ?? 0), min: Number(it.min ?? 0), max: Number(it.max ?? 1), step: Number(it.step ?? 0.01),
        });
    }
  };
  walk(json.ui, []);
  return out;
}
