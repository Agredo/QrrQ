// Turns a rendered or uploaded image back into a QR module grid so the managed
// QrrQ reader can decode it inside the browser - a real round trip, no server needed.
window.qrrq = (function () {
    'use strict';

    function loadImage(source) {
        return new Promise(function (resolve, reject) {
            const image = new Image();
            image.onload = function () { resolve(image); };
            image.onerror = function () { reject(new Error('The image could not be decoded.')); };
            image.src = source;
        });
    }

    function extract(image) {
        const width = image.naturalWidth || image.width;
        const height = image.naturalHeight || image.height;
        if (!width || !height) {
            throw new Error('The image is empty.');
        }

        const canvas = document.createElement('canvas');
        canvas.width = width;
        canvas.height = height;
        const context = canvas.getContext('2d', { willReadFrequently: true });
        context.imageSmoothingEnabled = false;
        context.drawImage(image, 0, 0, width, height);
        const pixels = context.getImageData(0, 0, width, height).data;

        function isDark(x, y) {
            const index = (y * width + x) * 4;
            if (pixels[index + 3] < 128) {
                return false;
            }

            const luminance = (0.299 * pixels[index] + 0.587 * pixels[index + 1] + 0.114 * pixels[index + 2]) / 255;
            return luminance < 0.5;
        }

        let minX = width, minY = height, maxX = -1, maxY = -1;
        for (let y = 0; y < height; y++) {
            for (let x = 0; x < width; x++) {
                if (!isDark(x, y)) {
                    continue;
                }

                if (x < minX) { minX = x; }
                if (x > maxX) { maxX = x; }
                if (y < minY) { minY = y; }
                if (y > maxY) { maxY = y; }
            }
        }

        if (maxX < 0) {
            throw new Error('The image does not contain any dark module.');
        }

        // The top border of the top-left finder pattern is seven modules wide and always dark,
        // so its run length gives the module size in pixels.
        let run = minX;
        while (run <= maxX && isDark(run, minY)) {
            run++;
        }

        const moduleSize = (run - minX) / 7;
        const size = Math.round((maxX - minX + 1) / moduleSize);
        if (!(moduleSize >= 0.5) || size < 21 || size > 177 || (size - 17) % 4 !== 0) {
            throw new Error('The image does not contain a QR symbol grid (' + (size || 0) + ' modules detected).');
        }

        function sample(cx, cy) {
            const offset = Math.max(1, moduleSize * 0.2);
            const points = [[0, 0], [-offset, 0], [offset, 0], [0, -offset], [0, offset]];
            let dark = 0;
            for (const point of points) {
                const x = Math.min(width - 1, Math.max(0, Math.round(cx + point[0])));
                const y = Math.min(height - 1, Math.max(0, Math.round(cy + point[1])));
                if (isDark(x, y)) {
                    dark++;
                }
            }

            return dark >= 3;
        }

        const matrix = [];
        for (let row = 0; row < size; row++) {
            let line = '';
            for (let column = 0; column < size; column++) {
                const x = minX + (column + 0.5) * moduleSize;
                const y = minY + (row + 0.5) * moduleSize;
                line += sample(x, y) ? '1' : '0';
            }

            matrix.push(line);
        }

        return { width: width, height: height, moduleSize: moduleSize, size: size, matrix: matrix };
    }

    async function decode(source) {
        try {
            const image = await loadImage(source);
            const result = extract(image);
            return {
                ok: true,
                error: null,
                width: result.width,
                height: result.height,
                moduleSize: result.moduleSize,
                size: result.size,
                matrix: result.matrix
            };
        } catch (error) {
            return {
                ok: false,
                error: error && error.message ? error.message : String(error),
                width: 0,
                height: 0,
                moduleSize: 0,
                size: 0,
                matrix: []
            };
        }
    }

    return { decode: decode };
})();
