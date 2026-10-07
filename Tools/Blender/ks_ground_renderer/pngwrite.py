"""Потоковая запись PNG большой карты: строки поступают полосами, сжатие —
zlib (C), фильтр Sub считается numpy. Весь файл в памяти не держится.
Файл пишется под временным именем и заменяет целевой только в finish()."""
import os
import struct
import zlib
from pathlib import Path

import numpy as np

SIGNATURE = b'\x89PNG\r\n\x1a\n'
COLOR_TYPES = {'G': 0, 'RGB': 2, 'GA': 4, 'RGBA': 6}
CHANNELS = {'G': 1, 'RGB': 3, 'GA': 2, 'RGBA': 4}


def _chunk(kind, data):
    return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xffffffff)


class PngStream:
    def __init__(self, path, width, height, bit_depth, mode, srgb=False, level=6):
        if bit_depth not in (8, 16) or mode not in COLOR_TYPES or width <= 0 or height <= 0:
            raise ValueError('Неверный формат PNG')
        self.path = Path(path)
        self.temp = self.path.with_name('.' + self.path.name + '.part')
        self.width, self.height, self.bit_depth, self.mode = width, height, bit_depth, mode
        self.bpp = CHANNELS[mode] * bit_depth // 8
        self.rows = 0
        self.file = open(self.temp, 'wb')
        self.file.write(SIGNATURE)
        self.file.write(_chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, bit_depth, COLOR_TYPES[mode], 0, 0, 0)))
        if srgb:
            self.file.write(_chunk(b'sRGB', b'\x00'))
        self.encoder = zlib.compressobj(level)
        self.buffer = bytearray()

    def write_rows(self, rows):
        """rows: numpy (n, width, channels) uint8 (8 бит) или uint16 (16 бит), сверху вниз."""
        n = rows.shape[0]
        if rows.shape[1] != self.width or rows.shape[2] != CHANNELS[self.mode]:
            raise ValueError('Строки PNG не совпадают с размером файла')
        if self.rows + n > self.height:
            raise ValueError('Лишние строки PNG')
        data = (rows.astype('>u2') if self.bit_depth == 16 else rows.astype(np.uint8)).reshape(n, -1).view(np.uint8)
        filtered = np.empty((n, data.shape[1] + 1), dtype=np.uint8)
        filtered[:, 0] = 1  # Sub: разность с левым пикселем
        filtered[:, 1:1 + self.bpp] = data[:, :self.bpp]
        filtered[:, 1 + self.bpp:] = data[:, self.bpp:] - data[:, :-self.bpp]
        self.buffer += self.encoder.compress(filtered.tobytes())
        if len(self.buffer) >= 1 << 20:
            self._flush()
        self.rows += n

    def _flush(self):
        if self.buffer:
            self.file.write(_chunk(b'IDAT', bytes(self.buffer)))
            self.buffer.clear()

    def finish(self):
        if self.rows != self.height:
            raise ValueError(f'PNG: записано строк {self.rows} из {self.height}')
        self.buffer += self.encoder.flush()
        self._flush()
        self.file.write(_chunk(b'IEND', b''))
        self.file.flush()
        os.fsync(self.file.fileno())
        self.file.close()
        os.replace(self.temp, self.path)

    def abort(self):
        try:
            self.file.close()
        finally:
            if self.temp.exists():
                self.temp.unlink()
