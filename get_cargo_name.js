x = // Rows

JSON.stringify(Object.fromEntries(new Map(Object.entries(x).map(([key, data]) => ([key, data.Name.LocalizedString ?? data.Name2.Texts.map(t => t.LocalizedString || t.CultureInvariantString).join(' ')])))))
