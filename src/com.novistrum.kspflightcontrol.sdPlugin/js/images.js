/**
 * Draws key images. Uses the plugin's own artwork where it exists and composes text on top,
 * falling back to a plain canvas tile when an asset is missing or unreadable.
 * Results are cached per frame so the 4 Hz poll never re-uploads an identical image.
 */
(function (root, factory) {
	const api = factory();
	if (typeof module === "object" && module.exports) {
		module.exports = api;
	} else {
		root.KspImages = api;
	}
})(typeof self !== "undefined" ? self : this, function () {
	const TILE_SIZE = 144;
	const FONT = "Segoe UI, Roboto, Helvetica, Arial, sans-serif";

	const assetCache = new Map();
	const assetPending = new Map();
	const imageElements = new Map();
	const tileCache = new Map();

	function attemptLoad(path, useCrossOrigin) {
		return new Promise(function (resolve) {
			const image = new Image();
			if (useCrossOrigin) {
				image.crossOrigin = "Anonymous";
			}
			image.onload = function () {
				resolve(image);
			};
			image.onerror = function () {
				resolve(null);
			};
			image.src = path;
		});
	}

	function toDataUrl(image) {
		try {
			const canvas = document.createElement("canvas");
			canvas.width = image.width || TILE_SIZE;
			canvas.height = image.height || TILE_SIZE;
			canvas.getContext("2d").drawImage(image, 0, 0);
			return canvas.toDataURL("image/png");
		} catch (cause) {
			// Canvas is tainted (file:// without file access); the caller retries or falls back.
			return null;
		}
	}

	function loadAsset(path) {
		if (assetCache.has(path)) {
			return Promise.resolve(assetCache.get(path));
		}
		if (assetPending.has(path)) {
			return assetPending.get(path);
		}

		// Plain load first: Stream Deck allows file access for plugin pages, and requesting
		// CORS (crossOrigin) is what breaks asset loading on file:// in other environments.
		const promise = attemptLoad(path, false)
			.then(function (image) {
				if (image) {
					const direct = toDataUrl(image);
					if (direct) {
						imageElements.set(path, image);
						return direct;
					}
				}

				return attemptLoad(path, true).then(function (corsImage) {
					if (!corsImage) {
						return null;
					}

					const converted = toDataUrl(corsImage);
					if (converted) {
						imageElements.set(path, corsImage);
					}
					return converted;
				});
			})
			.then(function (url) {
				assetCache.set(path, url);
				assetPending.delete(path);
				return url;
			})
			.catch(function () {
				assetCache.set(path, null);
				assetPending.delete(path);
				return null;
			});

		assetPending.set(path, promise);
		return promise;
	}

	function roundedRect(context, x, y, width, height, radius) {
		const r = Math.min(radius, width / 2, height / 2);
		context.beginPath();
		context.moveTo(x + r, y);
		context.lineTo(x + width - r, y);
		context.quadraticCurveTo(x + width, y, x + width, y + r);
		context.lineTo(x + width, y + height - r);
		context.quadraticCurveTo(x + width, y + height, x + width - r, y + height);
		context.lineTo(x + r, y + height);
		context.quadraticCurveTo(x, y + height, x, y + height - r);
		context.lineTo(x, y + r);
		context.quadraticCurveTo(x, y, x + r, y);
		context.closePath();
	}

	function tileSignature(tile) {
		return [
			tile.backgroundPath || "",
			tile.label || "",
			tile.value || "",
			tile.unit || "",
			tile.accent || "",
			tile.color || "",
			tile.background || "",
			tile.badge || "",
			tile.badgeColor || "",
			tile.valueSize || ""
		].join("|");
	}

	/** Returns a PNG data URL, or null when the requested background art is not loaded yet. */
	function renderTile(tile) {
		const key = tileSignature(tile);
		if (tileCache.has(key)) {
			return tileCache.get(key);
		}

		let artwork = null;
		if (tile.backgroundPath) {
			artwork = imageElements.get(tile.backgroundPath) || null;
			if (!artwork) {
				return null;
			}
		}

		const canvas = document.createElement("canvas");
		canvas.width = TILE_SIZE;
		canvas.height = TILE_SIZE;
		const context = canvas.getContext("2d");

		if (artwork) {
			try {
				context.drawImage(artwork, 0, 0, TILE_SIZE, TILE_SIZE);
			} catch (cause) {
				artwork = null;
			}
		}

		if (!artwork) {
			context.fillStyle = tile.background || "#0d1219";
			roundedRect(context, 3, 3, TILE_SIZE - 6, TILE_SIZE - 6, 16);
			context.fill();

			context.fillStyle = tile.accent || "#38bdf8";
			roundedRect(context, 3, 3, TILE_SIZE - 6, 12, 8);
			context.fill();
		} else {
			// Darken the artwork slightly so the readout stays legible.
			context.fillStyle = "rgba(6, 10, 16, 0.55)";
			roundedRect(context, 3, 3, TILE_SIZE - 6, TILE_SIZE - 6, 16);
			context.fill();

			context.fillStyle = tile.accent || "#38bdf8";
			roundedRect(context, 3, 3, TILE_SIZE - 6, 6, 3);
			context.fill();
		}

		context.textAlign = "center";
		context.textBaseline = "middle";

		context.fillStyle = "#c3d0de";
		if (tile.badge) {
			context.textAlign = "left";
			context.font = "600 15px " + FONT;
			context.fillText(tile.label || "", 14, 38, TILE_SIZE - 64);
			context.textAlign = "center";
		} else {
			context.font = "600 17px " + FONT;
			context.fillText(tile.label || "", TILE_SIZE / 2, 38, TILE_SIZE - 22);
		}

		context.fillStyle = tile.color || "#ffffff";
		context.font = "700 " + (tile.valueSize || 38) + "px " + FONT;
		context.fillText(tile.value || "", TILE_SIZE / 2, 78, TILE_SIZE - 18);

		context.fillStyle = "#a9b8c8";
		context.font = "500 17px " + FONT;
		context.fillText(tile.unit || "", TILE_SIZE / 2, 116, TILE_SIZE - 22);

		if (tile.badge) {
			context.fillStyle = tile.badgeColor || "#f59e0b";
			roundedRect(context, TILE_SIZE - 52, 20, 34, 20, 6);
			context.fill();
			context.fillStyle = "#0b1016";
			context.font = "700 12px " + FONT;
			context.fillText(tile.badge, TILE_SIZE - 35, 30, 30);
		}

		const url = canvas.toDataURL("image/png");
		tileCache.set(key, url);
		return url;
	}

	function createRenderer(streamDeck) {
		const applied = new Map();

		function apply(context, signature, action) {
			if (applied.get(context) === signature) {
				return;
			}
			applied.set(context, signature);
			action();
		}

		function key(context, spec) {
			const value = spec || {};
			const tile = {
				label: value.label || "",
				value: value.value || "",
				unit: value.unit || "",
				accent: value.accent,
				color: value.color,
				background: value.background,
				backgroundPath: value.backgroundImage,
				badge: value.badge,
				badgeColor: value.badgeColor,
				valueSize: value.valueSize
			};

			const signature = "tile|" + tileSignature(tile);

			function drawComposed() {
				const url = renderTile(tile);
				if (url) {
					apply(context, signature, function () {
						streamDeck.setImage(context, url);
						streamDeck.setTitle(context, "");
					});
					return true;
				}
				return false;
			}

			function drawArtwork() {
				const titleText = value.title || "";
				loadAsset(value.image).then(function (url) {
					if (url) {
						apply(context, "asset|" + value.image + "|" + titleText, function () {
							streamDeck.setImage(context, url);
							streamDeck.setTitle(context, titleText);
						});
						return;
					}

					const fallback = renderTile(Object.assign({}, tile, { backgroundPath: null }));
					apply(context, signature + "|fallback", function () {
						streamDeck.setImage(context, fallback);
						streamDeck.setTitle(context, "");
					});
				});
			}

			if (!value.backgroundImage && !value.image) {
				drawComposed();
				return;
			}

			// Artwork as the face of the key: text is composed onto it when possible,
			// otherwise the raw asset is shown with the Stream Deck title on top.
			if (value.backgroundImage) {
				loadAsset(value.backgroundImage).then(function () {
					if (!drawComposed() && value.image) {
						drawArtwork();
					}
				});

				if (!value.image) {
					return;
				}
			}

			drawArtwork();
		}

		function feedback(context, payload, signature) {
			apply(context, "feedback|" + (signature || JSON.stringify(payload)), function () {
				streamDeck.setFeedback(context, payload);
			});
		}

		function forget(context) {
			applied.delete(context);
		}

		return {
			key: key,
			feedback: feedback,
			forget: forget,
			renderTile: renderTile
		};
	}

	return {
		createRenderer: createRenderer,
		loadAsset: loadAsset,
		renderTile: renderTile,
		tileSignature: tileSignature,
		isLoaded: function (path) {
			return imageElements.has(path);
		}
	};
});
