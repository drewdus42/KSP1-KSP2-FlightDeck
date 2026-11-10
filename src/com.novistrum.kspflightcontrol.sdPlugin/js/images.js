/**
 * Draws key images: existing PNG assets when they exist, canvas tiles otherwise.
 * Results are cached per context so the 4 Hz poll does not re-upload identical images.
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
	const tileCache = new Map();

	function loadAsset(path) {
		if (assetCache.has(path)) {
			return Promise.resolve(assetCache.get(path));
		}
		if (assetPending.has(path)) {
			return assetPending.get(path);
		}

		const promise = new Promise(function (resolve) {
			const image = new Image();
			image.crossOrigin = "Anonymous";
			image.onload = function () {
				try {
					const canvas = document.createElement("canvas");
					canvas.width = image.width || TILE_SIZE;
					canvas.height = image.height || TILE_SIZE;
					canvas.getContext("2d").drawImage(image, 0, 0);
					const url = canvas.toDataURL("image/png");
					assetCache.set(path, url);
					assetPending.delete(path);
					resolve(url);
				} catch (cause) {
					assetCache.set(path, null);
					assetPending.delete(path);
					resolve(null);
				}
			};
			image.onerror = function () {
				assetCache.set(path, null);
				assetPending.delete(path);
				resolve(null);
			};
			image.src = path;
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

	function renderTile(tile) {
		const key = JSON.stringify(tile);
		if (tileCache.has(key)) {
			return tileCache.get(key);
		}

		const canvas = document.createElement("canvas");
		canvas.width = TILE_SIZE;
		canvas.height = TILE_SIZE;
		const context = canvas.getContext("2d");

		context.fillStyle = tile.background || "#0d1219";
		roundedRect(context, 3, 3, TILE_SIZE - 6, TILE_SIZE - 6, 16);
		context.fill();

		context.fillStyle = tile.accent || "#38bdf8";
		roundedRect(context, 3, 3, TILE_SIZE - 6, 12, 8);
		context.fill();

		context.textAlign = "center";
		context.textBaseline = "middle";

		context.fillStyle = "#93a4b8";
		context.font = "600 17px " + FONT;
		context.fillText(tile.label || "", TILE_SIZE / 2, 38, TILE_SIZE - 22);

		context.fillStyle = tile.color || "#f8fafc";
		context.font = "700 " + (tile.valueSize || 38) + "px " + FONT;
		context.fillText(tile.value || "", TILE_SIZE / 2, 78, TILE_SIZE - 18);

		context.fillStyle = "#7f8fa3";
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

		function drawTile(context, tile, signature) {
			apply(context, signature, function () {
				streamDeck.setImage(context, renderTile(tile));
				streamDeck.setTitle(context, "");
			});
		}

		/**
		 * spec: { image, title, label, value, unit, accent, color, badge }
		 * When the asset is unavailable the canvas tile fallback is drawn instead.
		 */
		function key(context, spec) {
			const value = spec || {};
			const tile = {
				label: value.label || "",
				value: value.value || "",
				unit: value.unit || "",
				accent: value.accent,
				color: value.color,
				background: value.background,
				badge: value.badge,
				badgeColor: value.badgeColor,
				valueSize: value.valueSize
			};

			if (!value.image) {
				drawTile(context, tile, "tile|" + JSON.stringify(tile));
				return;
			}

			const titleText = value.title || "";
			loadAsset(value.image).then(function (url) {
				if (url) {
					apply(context, "asset|" + value.image + "|" + titleText, function () {
						streamDeck.setImage(context, url);
						streamDeck.setTitle(context, titleText);
					});
				} else {
					drawTile(context, tile, "tile|" + JSON.stringify(tile));
				}
			});
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
		renderTile: renderTile
	};
});
