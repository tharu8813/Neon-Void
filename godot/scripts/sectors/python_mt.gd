extends RefCounted
## Python random.Random MT19937 integer seeding and _randbelow() for exact
## content.stages.plan_wave choice() parity. Not used for GPU visual RNG.
const N: int = 624
const M: int = 397
const MASK: int = 0xffffffff
const UPPER: int = 0x80000000
const LOWER: int = 0x7fffffff
const MATRIX: int = 0x9908b0df
var mt: Array[int] = []
var index: int = N

func _init() -> void:
    mt.resize(N)
    for i in range(N):
        mt[i] = 0

func seed_from_digest(digest: PackedByteArray) -> void:
    assert(digest.size() >= 8)
    # SHA-256 first 8 bytes interpreted as unsigned big-endian Python int.
    # Python's _randommodule.c splits PyLong into little-endian 32-bit limbs.
    var high: int = 0
    var low: int = 0
    for i in range(4):
        high = (high << 8) | int(digest[i])
        low = (low << 8) | int(digest[i + 4])
    var words: Array[int] = []
    words.append(low)
    if high > 0:
        words.append(high)
    seed_words(words)

func seed_words(words: Array[int]) -> void:
    mt[0] = 19650218
    for i in range(1, N):
        var previous: int = mt[i - 1]
        mt[i] = (1812433253 * (previous ^ (previous >> 30)) + i) & MASK
    var i: int = 1
    var j: int = 0
    for k in range(maxi(N, words.size())):
        var previous: int = mt[i - 1]
        mt[i] = ((mt[i] ^ ((previous ^ (previous >> 30)) * 1664525)) + words[j] + j) & MASK
        i += 1
        j += 1
        if i >= N:
            mt[0] = mt[N - 1]
            i = 1
        if j >= words.size():
            j = 0
    for k in range(N - 1):
        var previous: int = mt[i - 1]
        mt[i] = ((mt[i] ^ ((previous ^ (previous >> 30)) * 1566083941)) - i) & MASK
        i += 1
        if i >= N:
            mt[0] = mt[N - 1]
            i = 1
    mt[0] = 0x80000000
    index = N

func next_u32() -> int:
    if index >= N:
        for k in range(N - M):
            var y: int = (mt[k] & UPPER) | (mt[k + 1] & LOWER)
            mt[k] = mt[k + M] ^ (y >> 1) ^ (MATRIX if (y & 1) != 0 else 0)
        for k in range(N - M, N - 1):
            var y: int = (mt[k] & UPPER) | (mt[k + 1] & LOWER)
            mt[k] = mt[k + M - N] ^ (y >> 1) ^ (MATRIX if (y & 1) != 0 else 0)
        var y: int = (mt[N - 1] & UPPER) | (mt[0] & LOWER)
        mt[N - 1] = mt[M - 1] ^ (y >> 1) ^ (MATRIX if (y & 1) != 0 else 0)
        index = 0
    var value: int = mt[index]
    index += 1
    value ^= (value >> 11)
    value ^= (value << 7) & 0x9d2c5680
    value ^= (value << 15) & 0xefc60000
    value ^= (value >> 18)
    return value & MASK

func choice_index(length: int) -> int:
    assert(length > 0)
    var bits: int = 0
    var size: int = length
    while size > 0:
        bits += 1
        size >>= 1
    var selected: int = next_u32() >> (32 - bits)
    while selected >= length:
        selected = next_u32() >> (32 - bits)
    return selected
