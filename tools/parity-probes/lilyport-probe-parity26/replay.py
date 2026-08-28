#!/usr/bin/env python3
"""A LITERAL transcription of lily/skyline.cc's build path, in Python.

Purpose: bisect a skyline divergence.  Feed it the port's OWN dumped input
buildings and compare its answer with the port's OWN dumped output.  Python and
C# both use IEEE doubles, so a difference is an implementation difference and
not an arithmetic one -- which separates "the merge is unfaithful" from "the
inputs differ" without touching the oracle.

The only freedom in upstream's algorithm is std::sort's permutation of EQUAL
elements, so the sort is a parameter: 'stable' (what the port does) or
'libstdcxx' (introsort, reproduced).
"""
import math
import sys

INF = math.inf


class Building:
    __slots__ = ("left", "right", "slope", "y")

    def __init__(self, start, start_h, end_h, end):
        self.left = start
        self.right = end
        length = (end - start) if not (end < start) else 0.0
        slope = 0.0
        if start_h != end_h:
            slope = (end_h - start_h) / length if length != 0.0 else math.copysign(
                INF, end_h - start_h)
        self.slope = slope
        if math.isinf(start):
            self.y = start_h
        elif abs(slope) > 1e6:
            self.slope = 0.0
            self.y = max(start_h, end_h)
        else:
            self.y = start_h - slope * start

    def copy(self):
        b = Building.__new__(Building)
        b.left, b.right, b.slope, b.y = self.left, self.right, self.slope, self.y
        return b

    def height(self, x):
        return self.y if math.isinf(x) else self.slope * x + self.y

    def intersection_x(self, other):
        d = other.slope - self.slope
        if abs(d) < 1e-4:
            return max(self.left, other.left)
        return (self.y - other.y) / d

    def above(self, other, x):
        if math.isinf(self.y) or math.isinf(other.y) or math.isinf(x):
            return self.y > other.y
        return (self.slope - other.slope) * x + self.y > other.y

    def key(self):
        return (self.left, self.right, self.slope, self.y)

    def __repr__(self):
        return f"[{self.left!r},{self.right!r}] s={self.slope!r} y={self.y!r}"


def empty_skyline():
    return [Building(-INF, -INF, -INF, INF)]


def single_skyline(b):
    out = []
    if b.left != -INF:
        out.append(Building(-INF, -INF, -INF, b.left))
    out.append(b)
    if b.right != INF:
        out.append(Building(b.right, -INF, -INF, INF))
    return out


def non_overlapping_skyline(buildings):
    trimmed, result = [], []
    last_end = -INF
    last_building = Building(-INF, -INF, -INF, INF)
    for b in buildings:
        x1, x2 = b.left, b.right
        y1, y2 = b.height(x1), b.height(x2)
        if (last_building.height(x1) >= y1 and last_building.right >= x2
                and last_building.height(x2) >= y2):
            continue
        if x1 < last_end:
            trimmed.append(b)
            continue
        if x1 > last_end:
            result.append(Building(last_end, -INF, -INF, x1))
        result.append(b)
        last_building = b
        last_end = b.right
    if last_end < INF:
        result.append(Building(last_end, -INF, -INF, INF))
    return trimmed, result


def internal_merge_skyline(sbp, scp):
    result = []
    blist, clist = sbp, scp
    bi, ci = 0, 0
    b = blist[bi].copy()
    while ci < len(clist):
        c = clist[ci].copy()
        if b.right < c.right:                       # finish with b
            if b.right <= b.left:
                pass
            elif c.above(b, c.left):                # -|   . |
                m = b.copy()
                m.right = c.left
                if m.right > m.left:
                    result.append(m)
                if b.above(c, b.right):             # -|\--.
                    n = c.copy()
                    crossing = b.intersection_x(c)
                    n.right = crossing
                    b.left = crossing
                    result.append(n)
                    result.append(b.copy())
                    c.left = b.right
            else:
                if c.above(b, b.right):             # ---/ . |
                    crossing = b.intersection_x(c)
                    c.left = crossing
                    b.right = crossing
                else:                               # -----.
                    c.left = b.right
                result.append(b.copy())
            b = c
            bi, ci = ci, bi
            blist, clist = clist, blist
        else:                                       # finish with c
            if c.above(b, c.left):                  # -| |---.
                m = b.copy()
                m.right = c.left
                if m.right > m.left:
                    result.append(m)
                if b.above(c, c.right):             # -| \---.
                    c.right = b.intersection_x(c)
            elif c.above(b, c.right):               # ---/|--.
                m = b.copy()
                crossing = b.intersection_x(c)
                c.left = crossing
                m.right = crossing
                result.append(m)
            else:
                ci += 1
                continue
            result.append(c)
            b.left = c.right
        ci += 1
    if b.right > b.left:
        result.append(b)
    return result


def less_than(b1, b2):
    return (b1.left < b2.left
            or (b1.left == b2.left and b1.height(b1.left) > b2.height(b1.left)))


def sort_stable(items):
    import functools
    return sorted(items, key=functools.cmp_to_key(
        lambda a, b: -1 if less_than(a, b) else (1 if less_than(b, a) else 0)))


# ---- libstdc++ std::sort (introsort), transcribed from bits/stl_algo.h ----
THRESHOLD = 16


def _lg(n):
    return n.bit_length() - 1


def _move_median_to_first(a, result, b, c, d):
    if less_than(a[b], a[c]):
        if less_than(a[c], a[d]):
            a[result], a[c] = a[c], a[result]
        elif less_than(a[b], a[d]):
            a[result], a[d] = a[d], a[result]
        else:
            a[result], a[b] = a[b], a[result]
    elif less_than(a[b], a[d]):
        a[result], a[b] = a[b], a[result]
    elif less_than(a[c], a[d]):
        a[result], a[d] = a[d], a[result]
    else:
        a[result], a[c] = a[c], a[result]


def _unguarded_partition(a, first, last, pivot):
    while True:
        while less_than(a[first], a[pivot]):
            first += 1
        last -= 1
        while less_than(a[pivot], a[last]):
            last -= 1
        if not (first < last):
            return first
        a[first], a[last] = a[last], a[first]
        if pivot == first:
            pivot = last
        elif pivot == last:
            pivot = first
        first += 1


def _partition_pivot(a, first, last):
    mid = first + (last - first) // 2
    _move_median_to_first(a, first, first + 1, mid, last - 1)
    return _unguarded_partition(a, first + 1, last, first)


def _introsort_loop(a, first, last, depth):
    while last - first > THRESHOLD:
        if depth == 0:
            _heap_sort(a, first, last)
            return
        depth -= 1
        cut = _partition_pivot(a, first, last)
        _introsort_loop(a, cut, last, depth)
        last = cut


def _heap_sort(a, first, last):
    seg = sorted(a[first:last], key=__import__("functools").cmp_to_key(
        lambda x, y: -1 if less_than(x, y) else (1 if less_than(y, x) else 0)))
    a[first:last] = seg


def _unguarded_linear_insert(a, i):
    val = a[i]
    nxt = i - 1
    while less_than(val, a[nxt]):
        a[nxt + 1] = a[nxt]
        nxt -= 1
    a[nxt + 1] = val


def _insertion_sort(a, first, last):
    if first == last:
        return
    for i in range(first + 1, last):
        if less_than(a[i], a[first]):
            val = a[i]
            a[first + 1:i + 1] = a[first:i]
            a[first] = val
        else:
            _unguarded_linear_insert(a, i)


def _final_insertion_sort(a, first, last):
    if last - first > THRESHOLD:
        _insertion_sort(a, first, first + THRESHOLD)
        for i in range(first + THRESHOLD, last):
            _unguarded_linear_insert(a, i)
    else:
        _insertion_sort(a, first, last)


def sort_libstdcxx(items):
    a = list(items)
    if len(a) > 1:
        _introsort_loop(a, 0, len(a), _lg(len(a)) * 2)
        _final_insertion_sort(a, 0, len(a))
    return a


def internal_build_skyline(buildings, sorter):
    if len(buildings) == 0:
        return empty_skyline()
    if len(buildings) == 1:
        return single_skyline(buildings[0])
    partials = []
    work = sorter(buildings)
    while work:
        trimmed, partial = non_overlapping_skyline(work)
        partials.append(partial)
        work = trimmed
    while partials:
        one = partials.pop(0)
        if not partials:
            return one
        two = partials.pop(0)
        partials.append(internal_merge_skyline(one, two))
    raise AssertionError


def load(path):
    blocks, cur = [], None
    for line in open(path):
        if line.startswith("SKYLINE"):
            cur = {"hdr": line.strip(), "in": [], "out": []}
            blocks.append(cur)
        elif line.startswith("  IN "):
            v = [float(x) for x in line.split()[1:]]
            cur["in"].append(Building(v[0], v[1], v[2], v[3]))
        elif line.startswith("  OUT "):
            v = [float(x) for x in line.split()[1:]]
            b = Building.__new__(Building)
            b.left, b.right, b.slope, b.y = v[0], v[1], v[2], v[3]
            cur["out"].append(b)
    return blocks


if __name__ == "__main__":
    blocks = load(sys.argv[1])
    for i, blk in enumerate(blocks, 1):
        got_s = internal_build_skyline([b.copy() for b in blk["in"]], sort_stable)
        got_l = internal_build_skyline([b.copy() for b in blk["in"]], sort_libstdcxx)
        port = blk["out"]
        def close(u, v):
            if math.isinf(u) or math.isinf(v):
                return u == v
            return abs(u - v) < 1e-12

        def same(got):
            return len(got) == len(port) and all(
                a.left == b.left and a.right == b.right
                and a.slope == b.slope and a.y == b.y
                for a, b in zip(got, port))

        same_s, same_l = same(got_s), same(got_l)
        print(f"block {i}: inputs={len(blk['in'])} port_out={len(port)} "
              f"stable={len(got_s)} ({'==port' if same_s else 'DIFFERS'})  "
              f"libstdc++={len(got_l)} ({'==port' if same_l else 'DIFFERS'})")
