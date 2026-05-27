# -*- coding: utf-8 -*-
import io
import json
import hashlib
import math
import os
import sys
import traceback

import maya.standalone
maya.standalone.initialize(name="python")

import maya.cmds as cmds
import maya.mel as mel
import maya.api.OpenMaya as om

_BHV_BASE_WRITE_OK = 0
_BHV_BASE_WRITE_FALLBACK = 0


def get_cli_paths():
    args = sys.argv[1:]
    if len(args) < 2:
        raise RuntimeError("Need args: config_path result_path")
    return args[0], args[1]


def ensure_fbx_plugin():
    if not cmds.pluginInfo("fbxmaya", q=True, loaded=True):
        cmds.loadPlugin("fbxmaya", quiet=True)


def new_scene():
    cmds.file(new=True, force=True)


def import_fbx(path, namespace_name):
    return cmds.file(
        path,
        i=True,
        type="FBX",
        ignoreVersion=True,
        ra=True,
        mergeNamespacesOnClash=False,
        namespace=namespace_name,
        options="fbx",
        pr=True,
        returnNewNodes=True,
    )


def top_level_roots_from_nodes(nodes):
    if not nodes:
        return []
    trs = []
    seen = set()
    for n in nodes:
        try:
            if not cmds.objExists(n):
                continue
            if cmds.nodeType(n) != "transform":
                continue
            if n in seen:
                continue
            seen.add(n)
            trs.append(n)
        except Exception:
            pass
    tr_set = set(trs)
    roots = []
    for t in trs:
        parent = (cmds.listRelatives(t, parent=True, fullPath=True) or [None])[0]
        if parent not in tr_set:
            roots.append(t)
    return roots


def list_mesh_transforms(namespace_prefix=None):
    shapes = cmds.ls(type="mesh", long=True) or []
    result = []
    seen = set()
    ns = (namespace_prefix or "").lower()
    for shape in shapes:
        try:
            if cmds.getAttr(shape + ".intermediateObject"):
                continue
        except Exception:
            pass
        parents = cmds.listRelatives(shape, parent=True, fullPath=True) or []
        if not parents:
            continue
        tr = parents[0]
        if tr in seen:
            continue
        low = tr.lower()
        short_low = tr.split("|")[-1].lower()
        if ns and (ns + ":") not in low and not short_low.startswith(ns + ":"):
            continue
        seen.add(tr)
        result.append(tr)
    return result


def is_skinned_transform(mesh_tr):
    try:
        hist = cmds.listHistory(mesh_tr, pruneDagObjects=True) or []
        for node in hist:
            if cmds.nodeType(node) == "skinCluster":
                return True
    except Exception:
        pass
    return False


def resolve_mesh(token, namespace_prefix=None, exact_name=None):
    meshes = list_mesh_transforms(namespace_prefix)
    if not meshes:
        return None

    token_low = (token or "").lower()
    exact_low = (exact_name or "").lower()
    # 1) 先精确命中指定节点名
    if exact_low:
        for tr in meshes:
            short_low = tr.split("|")[-1].lower()
            if short_low == exact_low or short_low.endswith(":" + exact_low):
                return tr

    # 2) 再按 token 严格过滤
    token_hits = []
    if token_low:
        for tr in meshes:
            short_low = tr.split("|")[-1].lower()
            if token_low in short_low:
                token_hits.append(tr)
        if token_hits:
            # token 命中里选顶点最多的
            best = token_hits[0]
            best_v = -1
            for tr in token_hits:
                try:
                    vc = int(cmds.polyEvaluate(tr, vertex=True))
                except Exception:
                    vc = 0
                if vc > best_v:
                    best_v = vc
                    best = tr
            return best

    # 3) 禁止静默回退：未命中时仅返回 None，由上层中断。
    ns = namespace_prefix if namespace_prefix is not None else "<any>"
    sample = ", ".join([m.split("|")[-1] for m in meshes[:8]])
    if len(meshes) > 8:
        sample += ", ..."
    cmds.warning(
        "BHV resolve_mesh miss: token='{0}', exact='{1}', namespace='{2}', candidates={3}".format(
            token or "",
            exact_name or "",
            ns,
            sample or "<none>",
        )
    )
    return None


def get_world_positions(mesh_tr):
    count = int(cmds.polyEvaluate(mesh_tr, vertex=True))
    out = []
    for i in range(count):
        p = cmds.xform(f"{mesh_tr}.vtx[{i}]", q=True, ws=True, t=True)
        out.append((float(p[0]), float(p[1]), float(p[2])))
    return out


def parse_source_pose_offsets(cfg):
    idxs = cfg.get("sourcePoseOffsetIndices", []) or []
    vals = cfg.get("sourcePoseOffsetLocal", []) or []
    out = {}
    n = min(len(idxs), len(vals))
    for i in range(n):
        try:
            si = int(idxs[i])
        except Exception:
            continue
        if si < 0:
            continue
        raw = vals[i]
        x = y = z = None
        if isinstance(raw, dict):
            x = raw.get("x", None)
            y = raw.get("y", None)
            z = raw.get("z", None)
        elif isinstance(raw, (list, tuple)) and len(raw) >= 3:
            x, y, z = raw[0], raw[1], raw[2]
        else:
            try:
                x = getattr(raw, "x")
                y = getattr(raw, "y")
                z = getattr(raw, "z")
            except Exception:
                x = y = z = None
        if x is None or y is None or z is None:
            continue
        try:
            out[si] = (float(x), float(y), float(z))
        except Exception:
            pass
    return out


def local_offsets_to_world_vectors(mesh_tr, local_offsets):
    if not local_offsets:
        return {}
    out = {}
    try:
        tr_path = _get_dag_path(mesh_tr)
        m = tr_path.inclusiveMatrix()
        p0 = om.MPoint(0.0, 0.0, 0.0, 1.0) * m
    except Exception:
        m = None
        p0 = None
    for idx, v in local_offsets.items():
        try:
            lx = float(v[0])
            ly = float(v[1])
            lz = float(v[2])
        except Exception:
            continue
        if m is None or p0 is None:
            out[int(idx)] = (lx, ly, lz)
            continue
        p1 = om.MPoint(lx, ly, lz, 1.0) * m
        out[int(idx)] = (float(p1.x - p0.x), float(p1.y - p0.y), float(p1.z - p0.z))
    return out


def apply_offsets_to_source_positions(source_pos, world_offsets):
    if not world_offsets:
        return list(source_pos)
    out = list(source_pos)
    for si, v in world_offsets.items():
        if si < 0 or si >= len(out):
            continue
        sp = out[si]
        out[si] = (sp[0] + v[0], sp[1] + v[1], sp[2] + v[2])
    return out


def apply_global_offset(source_pos, global_offset):
    if not source_pos:
        return list(source_pos or [])
    if not global_offset:
        return list(source_pos)
    gx, gy, gz = parse_vec3(global_offset, (0.0, 0.0, 0.0))
    if abs(gx) <= 1e-12 and abs(gy) <= 1e-12 and abs(gz) <= 1e-12:
        return list(source_pos)
    out = [list(p) for p in source_pos]
    for i in range(len(out)):
        out[i][0] += gx
        out[i][1] += gy
        out[i][2] += gz
    return out


def parse_vec3(raw, fallback=(0.0, 0.0, 0.0)):
    try:
        if isinstance(raw, dict):
            return (
                float(raw.get("x", fallback[0])),
                float(raw.get("y", fallback[1])),
                float(raw.get("z", fallback[2])),
            )
        if isinstance(raw, (list, tuple)) and len(raw) >= 3:
            return (float(raw[0]), float(raw[1]), float(raw[2]))
    except Exception:
        return fallback
    return fallback


def blend_pos(base_pos, target_pos, scale):
    s = max(0.0, min(1.0, float(scale)))
    bx, by, bz = float(base_pos[0]), float(base_pos[1]), float(base_pos[2])
    tx, ty, tz = float(target_pos[0]), float(target_pos[1]), float(target_pos[2])
    return (
        bx + (tx - bx) * s,
        by + (ty - by) * s,
        bz + (tz - bz) * s,
    )


def set_world_position(mesh_tr, index, pos):
    global _BHV_BASE_WRITE_OK
    global _BHV_BASE_WRITE_FALLBACK
    wx = float(pos[0])
    wy = float(pos[1])
    wz = float(pos[2])
    # For skinned meshes, write to base/orig shape points so FBX+skin can preserve edits.
    if set_base_shape_world_position(mesh_tr, int(index), wx, wy, wz):
        _BHV_BASE_WRITE_OK += 1
        return
    _BHV_BASE_WRITE_FALLBACK += 1
    cmds.xform(f"{mesh_tr}.vtx[{index}]", ws=True, t=(wx, wy, wz))


def _get_dag_path(node_name):
    sel = om.MSelectionList()
    sel.add(node_name)
    return sel.getDagPath(0)


def _find_base_shape(mesh_tr):
    # Prefer intermediate/orig shape under the same transform.
    shapes = cmds.listRelatives(mesh_tr, shapes=True, fullPath=True) or []
    if not shapes:
        return None
    interm = []
    normal = []
    for sh in shapes:
        try:
            is_inter = bool(cmds.getAttr(sh + ".intermediateObject"))
        except Exception:
            is_inter = False
        if is_inter:
            interm.append(sh)
        else:
            normal.append(sh)
    if interm:
        # Prefer shapeOrig-like name if available.
        for sh in interm:
            if sh.lower().endswith("shapeorig"):
                return sh
        return interm[0]
    if normal:
        return normal[0]
    return shapes[0]


def set_base_shape_world_position(mesh_tr, index, wx, wy, wz):
    try:
        base_shape = _find_base_shape(mesh_tr)
        if not base_shape or not cmds.objExists(base_shape):
            return False
        mesh_path = _get_dag_path(base_shape)
        mesh_fn = om.MFnMesh(mesh_path)

        tr_path = _get_dag_path(mesh_tr)
        world_to_obj = tr_path.inclusiveMatrixInverse()
        p_obj = om.MPoint(wx, wy, wz, 1.0) * world_to_obj
        mesh_fn.setPoint(int(index), p_obj, om.MSpace.kObject)
        return True
    except Exception:
        return False


def get_base_shape_object_position(mesh_tr, index):
    try:
        base_shape = _find_base_shape(mesh_tr)
        if not base_shape or not cmds.objExists(base_shape):
            return None
        mesh_path = _get_dag_path(base_shape)
        mesh_fn = om.MFnMesh(mesh_path)
        p = mesh_fn.getPoint(int(index), om.MSpace.kObject)
        return (float(p.x), float(p.y), float(p.z))
    except Exception:
        return None


def set_base_shape_object_position(mesh_tr, index, ox, oy, oz):
    try:
        base_shape = _find_base_shape(mesh_tr)
        if not base_shape or not cmds.objExists(base_shape):
            return False
        mesh_path = _get_dag_path(base_shape)
        mesh_fn = om.MFnMesh(mesh_path)
        mesh_fn.setPoint(int(index), om.MPoint(float(ox), float(oy), float(oz), 1.0), om.MSpace.kObject)
        return True
    except Exception:
        return False


def get_world_normals(mesh_tr):
    count = int(cmds.polyEvaluate(mesh_tr, vertex=True))
    out = []
    for i in range(count):
        n = cmds.polyNormalPerVertex(f"{mesh_tr}.vtx[{i}]", q=True, xyz=True)
        if n and len(n) >= 3:
            sx = 0.0
            sy = 0.0
            sz = 0.0
            c = 0
            lim = len(n) - (len(n) % 3)
            for k in range(0, lim, 3):
                sx += float(n[k + 0])
                sy += float(n[k + 1])
                sz += float(n[k + 2])
                c += 1
            if c > 0:
                inv = 1.0 / float(c)
                x = sx * inv
                y = sy * inv
                z = sz * inv
                m = math.sqrt(x * x + y * y + z * z)
                if m > 1e-12:
                    out.append((x / m, y / m, z / m))
                    continue
        out.append((0.0, 1.0, 0.0))
    return out


def set_world_normal(mesh_tr, index, n):
    x = float(n[0])
    y = float(n[1])
    z = float(n[2])
    m = math.sqrt(x * x + y * y + z * z)
    if m > 1e-12:
        x /= m
        y /= m
        z /= m
    else:
        x, y, z = 0.0, 1.0, 0.0
    cmds.polyNormalPerVertex(
        f"{mesh_tr}.vtx[{index}]",
        xyz=(x, y, z),
    )


def get_vertex_world_position(mesh_tr, index):
    p = cmds.xform(f"{mesh_tr}.vtx[{index}]", q=True, ws=True, t=True)
    return (float(p[0]), float(p[1]), float(p[2]))


def solve_vertex_to_world_position(mesh_tr, index, desired_world, max_iter=6, tol=1e-4):
    dx = float(desired_world[0])
    dy = float(desired_world[1])
    dz = float(desired_world[2])
    final_err = float("inf")
    final_obs = get_vertex_world_position(mesh_tr, index)
    used_iter = 0
    damping = 1e-6
    eps = 1e-4

    def _solve_delta_from_jacobian(j_cols, err):
        # j_cols: [(jx0,jy0,jz0), (jx1,...), (jx2,...)] where each tuple is world delta from one object-axis perturb.
        # Solve (J^T J + lambda I) * d = J^T e
        j00, j10, j20 = j_cols[0][0], j_cols[0][1], j_cols[0][2]
        j01, j11, j21 = j_cols[1][0], j_cols[1][1], j_cols[1][2]
        j02, j12, j22 = j_cols[2][0], j_cols[2][1], j_cols[2][2]

        a00 = j00 * j00 + j10 * j10 + j20 * j20 + damping
        a01 = j00 * j01 + j10 * j11 + j20 * j21
        a02 = j00 * j02 + j10 * j12 + j20 * j22
        a11 = j01 * j01 + j11 * j11 + j21 * j21 + damping
        a12 = j01 * j02 + j11 * j12 + j21 * j22
        a22 = j02 * j02 + j12 * j12 + j22 * j22 + damping

        ex, ey, ez = err
        b0 = j00 * ex + j10 * ey + j20 * ez
        b1 = j01 * ex + j11 * ey + j21 * ez
        b2 = j02 * ex + j12 * ey + j22 * ez

        det = (
            a00 * (a11 * a22 - a12 * a12)
            - a01 * (a01 * a22 - a12 * a02)
            + a02 * (a01 * a12 - a11 * a02)
        )
        if abs(det) <= 1e-12:
            return None

        inv00 = (a11 * a22 - a12 * a12) / det
        inv01 = (a02 * a12 - a01 * a22) / det
        inv02 = (a01 * a12 - a02 * a11) / det
        inv11 = (a00 * a22 - a02 * a02) / det
        inv12 = (a02 * a01 - a00 * a12) / det
        inv22 = (a00 * a11 - a01 * a01) / det

        dxo = inv00 * b0 + inv01 * b1 + inv02 * b2
        dyo = inv01 * b0 + inv11 * b1 + inv12 * b2
        dzo = inv02 * b0 + inv12 * b1 + inv22 * b2
        return (dxo, dyo, dzo)

    for it in range(max(1, int(max_iter))):
        used_iter = it + 1
        obs = get_vertex_world_position(mesh_tr, index)
        ex = dx - obs[0]
        ey = dy - obs[1]
        ez = dz - obs[2]
        final_err = math.sqrt(ex * ex + ey * ey + ez * ez)
        final_obs = obs
        if final_err <= tol:
            break

        p0 = get_base_shape_object_position(mesh_tr, index)
        if p0 is None:
            # Fallback: legacy world-space fixed-point update.
            candidate = (obs[0] + ex, obs[1] + ey, obs[2] + ez)
            set_world_position(mesh_tr, index, candidate)
            continue

        px, py, pz = p0
        # Numerical Jacobian columns in world space.
        j_cols = []
        ok = True
        for ax in range(3):
            tx, ty, tz = px, py, pz
            if ax == 0:
                tx += eps
            elif ax == 1:
                ty += eps
            else:
                tz += eps
            if not set_base_shape_object_position(mesh_tr, index, tx, ty, tz):
                ok = False
                break
            o2 = get_vertex_world_position(mesh_tr, index)
            j_cols.append(
                (
                    (o2[0] - obs[0]) / eps,
                    (o2[1] - obs[1]) / eps,
                    (o2[2] - obs[2]) / eps,
                )
            )
            # Restore base point for next axis perturbation.
            set_base_shape_object_position(mesh_tr, index, px, py, pz)

        if not ok or len(j_cols) != 3:
            candidate = (obs[0] + ex, obs[1] + ey, obs[2] + ez)
            set_world_position(mesh_tr, index, candidate)
            continue

        d_obj = _solve_delta_from_jacobian(j_cols, (ex, ey, ez))
        if d_obj is None:
            candidate = (obs[0] + ex, obs[1] + ey, obs[2] + ez)
            set_world_position(mesh_tr, index, candidate)
            continue

        # Clamp step for stability.
        step_len = math.sqrt(d_obj[0] * d_obj[0] + d_obj[1] * d_obj[1] + d_obj[2] * d_obj[2])
        max_step = 5.0  # object-space units (cm in this pipeline)
        if step_len > max_step and step_len > 1e-12:
            s = max_step / step_len
            d_obj = (d_obj[0] * s, d_obj[1] * s, d_obj[2] * s)

        set_base_shape_object_position(mesh_tr, index, px + d_obj[0], py + d_obj[1], pz + d_obj[2])
    return used_iter, final_err, final_obs


def compensate_pairs_after_restore(src_mesh, tgt_mesh, mapped_pairs, source_pos_override=None, max_iter=6, tol=1e-4):
    src_pos = source_pos_override if source_pos_override is not None else get_world_positions(src_mesh)
    total = 0
    solved = 0
    iter_sum = 0
    err_sum = 0.0
    err_max = 0.0
    for ti, si in mapped_pairs:
        if ti < 0 or si < 0:
            continue
        if si >= len(src_pos):
            continue
        desired = src_pos[si]
        used_iter, final_err, _obs = solve_vertex_to_world_position(tgt_mesh, int(ti), desired, max_iter=max_iter, tol=tol)
        total += 1
        iter_sum += int(used_iter)
        err_sum += float(final_err)
        if final_err > err_max:
            err_max = float(final_err)
        if final_err <= tol:
            solved += 1

    avg_iter = (float(iter_sum) / float(total)) if total > 0 else 0.0
    avg_err = (float(err_sum) / float(total)) if total > 0 else 0.0
    tgt_pos = get_world_positions(tgt_mesh)
    verify_after = verify_pairs(src_pos, tgt_pos, mapped_pairs)
    return {
        "count": int(total),
        "solved": int(solved),
        "solveRate": float(float(solved) / float(total)) if total > 0 else 0.0,
        "avgIter": float(avg_iter),
        "avgErr": float(avg_err),
        "maxErr": float(err_max),
        "verifyAfter": verify_after,
        "tol": float(tol),
        "maxIter": int(max_iter),
    }


def apply_vertex_markers(mesh_tr, mapped_pairs):
    # Encode mapped source index into vertex color:
    # R=1.0(marker), G/B = srcIdx high/low byte
    if not mapped_pairs:
        return 0, 0, "no_pairs"
    try:
        cmds.polyColorSet(mesh_tr, create=True, colorSet="BHVMap")
    except Exception:
        pass
    try:
        cmds.polyColorSet(mesh_tr, currentColorSet=True, colorSet="BHVMap")
    except Exception:
        return 0, 0, "color_set_failed"

    tagged = 0
    uniq = set()
    failed = 0
    for ti, si, _ in mapped_pairs:
        src_u16 = int(si) & 0xFFFF
        hi = (src_u16 >> 8) & 0xFF
        lo = src_u16 & 0xFF
        comp = "{0}.vtx[{1}]".format(mesh_tr, int(ti))
        try:
            cmds.polyColorPerVertex(comp, rgb=(1.0, hi / 255.0, lo / 255.0), a=1.0, cdo=True)
            tagged += 1
            uniq.add(src_u16)
        except Exception:
            failed += 1
    if failed > 0:
        return tagged, len(uniq), "partial_failed({0})".format(failed)
    return tagged, len(uniq), "ok"


def clear_vertex_markers(mesh_tr, color_set_name="BHVMap"):
    removed = 0
    try:
        color_sets = cmds.polyColorSet(mesh_tr, q=True, allColorSets=True) or []
    except Exception:
        color_sets = []
    for cs in list(color_sets):
        if str(cs) != str(color_set_name):
            continue
        try:
            cmds.polyColorSet(mesh_tr, delete=True, colorSet=color_set_name)
            removed += 1
        except Exception:
            pass
    return removed


def clear_target_namespace_markers(namespace_prefix, color_set_name="BHVMap"):
    meshes = list_mesh_transforms(namespace_prefix)
    mesh_count = 0
    removed_sets = 0
    for tr in meshes:
        mesh_count += 1
        removed_sets += clear_vertex_markers(tr, color_set_name)
    return mesh_count, removed_sets


def list_mesh_transforms_under_roots(roots):
    if not roots:
        return []
    out = []
    seen = set()
    for r in roots:
        if not r or not cmds.objExists(r):
            continue
        try:
            root_type = cmds.nodeType(r)
        except Exception:
            root_type = ""
        transforms = []
        if root_type == "transform":
            transforms.append(r)
        children = cmds.listRelatives(r, ad=True, type="transform", fullPath=True) or []
        transforms.extend(children)
        for tr in transforms:
            if tr in seen:
                continue
            seen.add(tr)
            shapes = cmds.listRelatives(tr, shapes=True, noIntermediate=True, fullPath=True) or []
            has_mesh = False
            for sh in shapes:
                try:
                    if cmds.nodeType(sh) == "mesh":
                        has_mesh = True
                        break
                except Exception:
                    pass
            if has_mesh:
                out.append(tr)
    return out


def clear_markers_on_meshes(meshes, color_set_name="BHVMap"):
    mesh_count = 0
    removed_sets = 0
    for tr in meshes or []:
        mesh_count += 1
        removed_sets += clear_vertex_markers(tr, color_set_name)
    return mesh_count, removed_sets


def decode_marker_pairs(mesh_tr, source_count):
    pairs = []
    bad = 0
    count = int(cmds.polyEvaluate(mesh_tr, vertex=True))
    for ti in range(count):
        comp = "{0}.vtx[{1}]".format(mesh_tr, ti)
        try:
            c = cmds.polyColorPerVertex(comp, q=True, rgb=True) or []
        except Exception:
            c = []
        if len(c) < 3:
            continue
        # vertex may have multiple face-vertex colors; take first triplet
        r = float(c[0])
        g = float(c[1])
        b = float(c[2])
        if r < 0.98:
            continue
        hi = int(round(max(0.0, min(1.0, g)) * 255.0))
        lo = int(round(max(0.0, min(1.0, b)) * 255.0))
        si = ((hi << 8) | lo) & 0xFFFF
        if si < 0 or si >= source_count:
            bad += 1
            continue
        pairs.append((ti, si))
    return pairs, bad


def apply_marker_driven_correction(src_mesh, tgt_mesh, copy_pos, copy_nrm, copy_weights, pos_gain=1.0, original_target_pos=None):
    src_pos = get_world_positions(src_mesh)
    src_nrm = get_world_normals(src_mesh) if copy_nrm else []
    pairs, bad = decode_marker_pairs(tgt_mesh, len(src_pos))
    if not pairs:
        return {
            "markerPairs": 0,
            "markerBad": bad,
            "markerMoved": 0,
            "markerWeightCopied": 0,
            "markerWeightStatus": "no_marker_pairs",
        }

    wc = 0
    ws = "disabled"
    # 先写权重，再做最终位置吸附，避免“写完权重又把点拉走”。
    if copy_weights:
        wc, ws = copy_skin_weights_for_pairs(src_mesh, tgt_mesh, pairs)

    moved = 0
    for ti, si in pairs:
        tp = cmds.xform("{0}.vtx[{1}]".format(tgt_mesh, ti), q=True, ws=True, t=True)
        sp = src_pos[si]
        tx = sp[0]
        ty = sp[1]
        tz = sp[2]
        if original_target_pos is not None and 0 <= ti < len(original_target_pos):
            op = original_target_pos[ti]
            g = float(pos_gain)
            tx = op[0] + (sp[0] - op[0]) * g
            ty = op[1] + (sp[1] - op[1]) * g
            tz = op[2] + (sp[2] - op[2]) * g
        dx = tx - float(tp[0])
        dy = ty - float(tp[1])
        dz = tz - float(tp[2])
        d = math.sqrt(dx * dx + dy * dy + dz * dz)
        if copy_pos and d > 1e-9:
            set_world_position(tgt_mesh, ti, (tx, ty, tz))
            moved += 1
        if copy_nrm and si < len(src_nrm):
            set_world_normal(tgt_mesh, ti, src_nrm[si])

    return {
        "markerPairs": len(pairs),
        "markerBad": bad,
        "markerMoved": moved,
        "markerWeightCopied": wc,
        "markerWeightStatus": ws,
        "markerPosGain": float(pos_gain),
    }


def strip_skin_cluster(mesh_tr):
    try:
        hist = cmds.listHistory(mesh_tr, pruneDagObjects=True) or []
    except Exception:
        hist = []
    removed = False
    for node in hist:
        try:
            if cmds.nodeType(node) == "skinCluster":
                cmds.skinCluster(node, e=True, unbind=True)
                removed = True
        except Exception:
            pass
    return removed


def canonical_influence_name(name):
    if not name:
        return ""
    n = str(name).split("|")[-1]
    if ":" in n:
        n = n.split(":")[-1]
    return n.lower()


def find_skin_cluster(mesh_tr):
    try:
        hist = cmds.listHistory(mesh_tr, pruneDagObjects=True) or []
    except Exception:
        hist = []
    for node in hist:
        try:
            if cmds.nodeType(node) == "skinCluster":
                return node
        except Exception:
            pass
    return None


def list_skin_clusters(mesh_tr):
    out = []
    try:
        hist = cmds.listHistory(mesh_tr, pruneDagObjects=True) or []
    except Exception:
        hist = []
    seen = set()
    for node in hist:
        try:
            if cmds.nodeType(node) == "skinCluster" and node not in seen:
                seen.add(node)
                out.append(node)
        except Exception:
            pass
    return out


def capture_skincluster_envelopes(meshes):
    state = {}
    for mesh_tr in meshes:
        for sc in list_skin_clusters(mesh_tr):
            if sc in state:
                continue
            try:
                state[sc] = float(cmds.getAttr(sc + ".envelope"))
            except Exception:
                pass
    return state


def set_skincluster_envelopes(state, value):
    if not state:
        return "none", 0
    ok = 0
    failed = 0
    v = float(value)
    for sc in state.keys():
        try:
            if not cmds.objExists(sc):
                continue
            cmds.setAttr(sc + ".envelope", v)
            ok += 1
        except Exception:
            failed += 1
    return "ok(set={0},failed={1},value={2:.3f})".format(ok, failed, v), ok


def restore_skincluster_envelopes(state):
    if not state:
        return "none", 0
    ok = 0
    failed = 0
    for sc, v in state.items():
        try:
            if not cmds.objExists(sc):
                continue
            cmds.setAttr(sc + ".envelope", float(v))
            ok += 1
        except Exception:
            failed += 1
    return "ok(restored={0},failed={1})".format(ok, failed), ok


def list_bind_poses_for_skincluster(sc):
    poses = []
    seen = set()
    # common connection path
    for attr in ("bindPose", "message"):
        try:
            conns = cmds.listConnections("{0}.{1}".format(sc, attr), s=True, d=False) or []
        except Exception:
            conns = []
        for n in conns:
            try:
                if cmds.nodeType(n) == "dagPose" and n not in seen:
                    seen.add(n)
                    poses.append(n)
            except Exception:
                pass
    # fallback scan history
    try:
        hist = cmds.listHistory(sc) or []
    except Exception:
        hist = []
    for n in hist:
        try:
            if cmds.nodeType(n) == "dagPose" and n not in seen:
                seen.add(n)
                poses.append(n)
        except Exception:
            pass
    return poses


def capture_joint_world_matrices(meshes):
    joints = []
    seen = set()
    for mesh_tr in meshes:
        for sc in list_skin_clusters(mesh_tr):
            infs = cmds.skinCluster(sc, q=True, inf=True) or []
            for j in infs:
                if j in seen:
                    continue
                seen.add(j)
                joints.append(j)
    state = {}
    for j in joints:
        try:
            m = cmds.xform(j, q=True, ws=True, m=True)
            state[j] = [float(x) for x in m]
        except Exception:
            pass
    return state


def restore_joint_world_matrices(state):
    if not state:
        return 0
    ok = 0
    for j, m in state.items():
        try:
            if not cmds.objExists(j):
                continue
            cmds.xform(j, ws=True, m=m)
            ok += 1
        except Exception:
            pass
    return ok


def set_meshes_to_bind_pose(meshes):
    restored = 0
    failed = 0
    seen = set()
    for mesh_tr in meshes:
        for sc in list_skin_clusters(mesh_tr):
            poses = list_bind_poses_for_skincluster(sc)
            for p in poses:
                if p in seen:
                    continue
                seen.add(p)
                try:
                    cmds.dagPose(p, restore=True, g=True)
                    restored += 1
                except Exception:
                    failed += 1
    status = "ok(restored={0},failed={1})".format(restored, failed)
    return status


def copy_skin_weights_for_pairs(src_mesh, tgt_mesh, mapped_pairs):
    if not mapped_pairs:
        return 0, "no_pairs"

    src_sc = find_skin_cluster(src_mesh)
    tgt_sc = find_skin_cluster(tgt_mesh)
    if not src_sc:
        return 0, "source_no_skinCluster"
    if not tgt_sc:
        return 0, "target_no_skinCluster"

    src_infs = cmds.skinCluster(src_sc, q=True, inf=True) or []
    tgt_infs = cmds.skinCluster(tgt_sc, q=True, inf=True) or []
    if not src_infs:
        return 0, "source_no_influences"
    if not tgt_infs:
        return 0, "target_no_influences"

    tgt_by_canon = {}
    for inf in tgt_infs:
        c = canonical_influence_name(inf)
        if c and c not in tgt_by_canon:
            tgt_by_canon[c] = inf

    copied = 0
    no_match = 0
    failed = 0
    for ti, si in mapped_pairs:
        src_comp = "{0}.vtx[{1}]".format(src_mesh, int(si))
        tgt_comp = "{0}.vtx[{1}]".format(tgt_mesh, int(ti))
        try:
            src_vals = cmds.skinPercent(src_sc, src_comp, q=True, value=True) or []
        except Exception:
            failed += 1
            continue
        if len(src_vals) != len(src_infs):
            failed += 1
            continue

        mapped = {}
        for inf, w in zip(src_infs, src_vals):
            wf = float(w)
            if wf <= 0.0:
                continue
            tgt_inf = tgt_by_canon.get(canonical_influence_name(inf))
            if not tgt_inf:
                continue
            mapped[tgt_inf] = mapped.get(tgt_inf, 0.0) + wf

        if not mapped:
            no_match += 1
            continue

        total = sum(mapped.values())
        if total <= 1e-12:
            no_match += 1
            continue

        tv = []
        for inf in tgt_infs:
            tv.append((inf, mapped.get(inf, 0.0) / total))

        try:
            cmds.skinPercent(tgt_sc, tgt_comp, transformValue=tv, normalize=True)
            copied += 1
        except Exception:
            failed += 1

    status = "ok"
    if copied == 0 and no_match > 0 and failed == 0:
        status = "no_influence_match"
    if failed > 0:
        status = "partial_failed"
    status = "{0}(copied={1},noMatch={2},failed={3})".format(status, copied, no_match, failed)
    return copied, status


def zero_skin_weights(mesh_tr):
    sc = find_skin_cluster(mesh_tr)
    if not sc:
        return 0, "no_skinCluster"
    infs = cmds.skinCluster(sc, q=True, inf=True) or []
    if not infs:
        return 0, "no_influences"

    try:
        old_norm = int(cmds.getAttr(sc + ".normalizeWeights"))
    except Exception:
        old_norm = None
    try:
        cmds.setAttr(sc + ".normalizeWeights", 0)
    except Exception:
        pass

    count = int(cmds.polyEvaluate(mesh_tr, vertex=True))
    ok = 0
    failed = 0
    tv = [(inf, 0.0) for inf in infs]
    for vi in range(count):
        comp = "{0}.vtx[{1}]".format(mesh_tr, vi)
        try:
            cmds.skinPercent(sc, comp, transformValue=tv, normalize=False)
            ok += 1
        except Exception:
            failed += 1

    if old_norm is not None:
        try:
            cmds.setAttr(sc + ".normalizeWeights", old_norm)
        except Exception:
            pass

    return ok, "ok(vertices={0},failed={1})".format(ok, failed)


def force_absurd_weights_for_pairs(tgt_mesh, mapped_pairs, bone_hint):
    if not mapped_pairs:
        return 0, "", "no_pairs"
    sc = find_skin_cluster(tgt_mesh)
    if not sc:
        return 0, "", "no_skinCluster"
    infs = cmds.skinCluster(sc, q=True, inf=True) or []
    if not infs:
        return 0, "", "no_influences"

    chosen = None
    hint = canonical_influence_name(bone_hint)
    if hint:
        for inf in infs:
            if canonical_influence_name(inf) == hint:
                chosen = inf
                break
    if chosen is None:
        chosen = infs[0]

    try:
        old_norm = int(cmds.getAttr(sc + ".normalizeWeights"))
    except Exception:
        old_norm = None
    try:
        cmds.setAttr(sc + ".normalizeWeights", 0)
    except Exception:
        pass

    ok = 0
    failed = 0
    tv = []
    for inf in infs:
        tv.append((inf, 1.0 if inf == chosen else 0.0))
    for ti, _, _ in mapped_pairs:
        comp = "{0}.vtx[{1}]".format(tgt_mesh, int(ti))
        try:
            cmds.skinPercent(sc, comp, transformValue=tv, normalize=False)
            ok += 1
        except Exception:
            failed += 1

    if old_norm is not None:
        try:
            cmds.setAttr(sc + ".normalizeWeights", old_norm)
        except Exception:
            pass

    status = "ok(mapped={0},failed={1})".format(ok, failed)
    return ok, str(chosen), status


def force_absurd_weights_for_all_vertices(tgt_mesh, bone_hint):
    sc = find_skin_cluster(tgt_mesh)
    if not sc:
        return 0, "", "no_skinCluster"
    infs = cmds.skinCluster(sc, q=True, inf=True) or []
    if not infs:
        return 0, "", "no_influences"

    chosen = None
    hint = canonical_influence_name(bone_hint)
    if hint:
        for inf in infs:
            if canonical_influence_name(inf) == hint:
                chosen = inf
                break
    if chosen is None:
        chosen = infs[0]

    try:
        old_norm = int(cmds.getAttr(sc + ".normalizeWeights"))
    except Exception:
        old_norm = None
    try:
        cmds.setAttr(sc + ".normalizeWeights", 0)
    except Exception:
        pass

    count = int(cmds.polyEvaluate(tgt_mesh, vertex=True))
    ok = 0
    failed = 0
    tv = []
    for inf in infs:
        tv.append((inf, 1.0 if inf == chosen else 0.0))
    for vi in range(count):
        comp = "{0}.vtx[{1}]".format(tgt_mesh, vi)
        try:
            cmds.skinPercent(sc, comp, transformValue=tv, normalize=False)
            ok += 1
        except Exception:
            failed += 1

    if old_norm is not None:
        try:
            cmds.setAttr(sc + ".normalizeWeights", old_norm)
        except Exception:
            pass

    status = "ok(all={0},failed={1})".format(ok, failed)
    return ok, str(chosen), status


def get_boundary_vertex_indices(mesh_tr):
    out = set()
    if not mesh_tr or not cmds.objExists(mesh_tr):
        return out
    try:
        edge_comp = cmds.polyListComponentConversion(mesh_tr, toEdge=True, border=True) or []
        edge_comp = cmds.filterExpand(edge_comp, sm=32) or []
        if not edge_comp:
            return out
        vtx_comp = cmds.polyListComponentConversion(edge_comp, toVertex=True) or []
        vtx_comp = cmds.filterExpand(vtx_comp, sm=31) or []
        for c in vtx_comp:
            m = re.search(r"\.vtx\[(\d+)\]", str(c))
            if not m:
                continue
            out.add(int(m.group(1)))
    except Exception:
        return set()
    return out


def build_threshold_pairs(source_pos, target_pos, threshold_cm, excluded_source_indices=None, excluded_target_indices=None, allowed_source_indices=None):
    # 多对一：每个 target 找最近 source；若在阈值内则吸附
    pairs = []
    nearest_max = 0.0
    if not source_pos or not target_pos:
        return pairs, nearest_max
    excluded = set()
    for si in excluded_source_indices or []:
        try:
            excluded.add(int(si))
        except Exception:
            pass
    excluded_t = set()
    for ti in excluded_target_indices or []:
        try:
            excluded_t.add(int(ti))
        except Exception:
            pass
    th2 = threshold_cm * threshold_cm
    allowed = None
    if allowed_source_indices is not None:
        allowed = set()
        for si in allowed_source_indices:
            try:
                allowed.add(int(si))
            except Exception:
                pass
        if not allowed:
            allowed = None
    for ti, tp in enumerate(target_pos):
        if ti in excluded_t:
            continue
        best_si = -1
        best_d2 = float("inf")
        for si, sp in enumerate(source_pos):
            if allowed is not None and si not in allowed:
                continue
            if si in excluded:
                continue
            dx = tp[0] - sp[0]
            dy = tp[1] - sp[1]
            dz = tp[2] - sp[2]
            d2 = dx * dx + dy * dy + dz * dz
            if d2 < best_d2:
                best_d2 = d2
                best_si = si
        if best_si < 0:
            continue
        d = math.sqrt(best_d2)
        if d > nearest_max:
            nearest_max = d
        if best_d2 <= th2:
            pairs.append((ti, best_si, best_d2))
    return pairs, nearest_max


def _quantize_pos_key(p, eps):
    if eps <= 0.0:
        eps = 1e-6
    return (
        int(round(float(p[0]) / eps)),
        int(round(float(p[1]) / eps)),
        int(round(float(p[2]) / eps)),
    )


def expand_pairs_to_coincident_targets(pairs, source_pos, target_pos, eps=1e-5, excluded_target_indices=None):
    if not pairs or not source_pos or not target_pos:
        return list(pairs or []), 0

    excluded_t = set()
    for ti in excluded_target_indices or []:
        try:
            excluded_t.add(int(ti))
        except Exception:
            pass

    buckets = {}
    for ti, tp in enumerate(target_pos):
        k = _quantize_pos_key(tp, eps)
        arr = buckets.get(k)
        if arr is None:
            buckets[k] = [ti]
        else:
            arr.append(ti)

    best_by_target = {}
    for ti, si, d2 in pairs:
        if ti in excluded_t:
            continue
        if ti not in best_by_target or d2 < best_by_target[ti][1]:
            best_by_target[ti] = (si, d2)

    seed_pairs = list(best_by_target.items())
    for ti, (si, _d2_seed) in seed_pairs:
        if ti < 0 or ti >= len(target_pos):
            continue
        if si < 0 or si >= len(source_pos):
            continue
        key = _quantize_pos_key(target_pos[ti], eps)
        neighbors = buckets.get(key, [])
        sp = source_pos[si]
        for tj in neighbors:
            if tj in excluded_t:
                continue
            if tj < 0 or tj >= len(target_pos):
                continue
            tpj = target_pos[tj]
            dx = tpj[0] - sp[0]
            dy = tpj[1] - sp[1]
            dz = tpj[2] - sp[2]
            d2 = dx * dx + dy * dy + dz * dz
            if tj not in best_by_target or d2 < best_by_target[tj][1]:
                best_by_target[tj] = (si, d2)

    out_pairs = []
    for ti in sorted(best_by_target.keys()):
        si, d2 = best_by_target[ti]
        out_pairs.append((ti, si, d2))
    added = max(0, len(out_pairs) - len(pairs))
    return out_pairs, added


def build_non_isolated_source_indices(source_pos):
    if not source_pos:
        return set(), {"count": 0, "kept": 0, "isolated": 0}
    n = len(source_pos)
    if n <= 2:
        return set(range(n)), {"count": n, "kept": n, "isolated": 0}

    nearest = []
    for i in range(n):
        pi = source_pos[i]
        best = float("inf")
        for j in range(n):
            if i == j:
                continue
            pj = source_pos[j]
            dx = pi[0] - pj[0]
            dy = pi[1] - pj[1]
            dz = pi[2] - pj[2]
            d2 = dx * dx + dy * dy + dz * dz
            if d2 < best:
                best = d2
        nearest.append(math.sqrt(best) if best < float("inf") else 0.0)

    vals = sorted(nearest)
    q1 = percentile(vals, 0.25)
    q3 = percentile(vals, 0.75)
    iqr = max(0.0, q3 - q1)
    th = q3 + iqr * 1.5
    keep = set()
    iso = 0
    for i, d in enumerate(nearest):
        if d <= th:
            keep.add(i)
        else:
            iso += 1
    if not keep:
        keep = set(range(n))
        iso = 0
    return keep, {"count": n, "kept": len(keep), "isolated": iso}


def percentile(sorted_vals, ratio):
    if not sorted_vals:
        return 0.0
    idx = int(math.ceil(len(sorted_vals) * ratio) - 1)
    idx = max(0, min(idx, len(sorted_vals) - 1))
    return float(sorted_vals[idx])


def verify_pairs(source_pos, target_pos, mapped_pairs):
    diffs = []
    for ti, si in mapped_pairs:
        if ti < 0 or si < 0:
            continue
        if ti >= len(target_pos) or si >= len(source_pos):
            continue
        tp = target_pos[ti]
        sp = source_pos[si]
        dx = tp[0] - sp[0]
        dy = tp[1] - sp[1]
        dz = tp[2] - sp[2]
        diffs.append(math.sqrt(dx * dx + dy * dy + dz * dz))
    if not diffs:
        return {"count": 0, "max": 0.0, "avg": 0.0, "p95": 0.0, "snap": 0.0}
    diffs.sort()
    snap = 0
    for d in diffs:
        if d <= 1e-6:
            snap += 1
    return {
        "count": len(diffs),
        "max": float(diffs[-1]),
        "avg": float(sum(diffs) / len(diffs)),
        "p95": float(percentile(diffs, 0.95)),
        "snap": float(snap / len(diffs)),
    }


def verify_nearest(source_pos, target_pos, mapped_pairs):
    diffs = []
    for ti, _ in mapped_pairs:
        if ti < 0 or ti >= len(target_pos):
            continue
        tp = target_pos[ti]
        best_d2 = float("inf")
        for sp in source_pos:
            dx = tp[0] - sp[0]
            dy = tp[1] - sp[1]
            dz = tp[2] - sp[2]
            d2 = dx * dx + dy * dy + dz * dz
            if d2 < best_d2:
                best_d2 = d2
        diffs.append(math.sqrt(best_d2))
    if not diffs:
        return {"count": 0, "max": 0.0, "avg": 0.0, "p95": 0.0, "snap": 0.0}
    diffs.sort()
    snap = 0
    for d in diffs:
        if d <= 1e-6:
            snap += 1
    return {
        "count": len(diffs),
        "max": float(diffs[-1]),
        "avg": float(sum(diffs) / len(diffs)),
        "p95": float(percentile(diffs, 0.95)),
        "snap": float(snap / len(diffs)),
    }


def choose_verify_pairs(tgt_mesh, source_count, fallback_pairs, prefer_marker):
    pairs = list(fallback_pairs or [])
    mode = "original"
    marker_bad = 0
    if prefer_marker:
        marker_pairs, marker_bad = decode_marker_pairs(tgt_mesh, source_count)
        marker_bad = int(marker_bad)
        if marker_pairs:
            pairs = marker_pairs
            mode = "marker"
    return pairs, mode, marker_bad


def _safe_get_attr(attr_name, default=None):
    try:
        return cmds.getAttr(attr_name)
    except Exception:
        return default


def get_mesh_signature(mesh_tr):
    sig = {
        "mesh": str(mesh_tr or ""),
        "v": 0,
        "e": 0,
        "f": 0,
        "bbox": [0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
        "size": [0.0, 0.0, 0.0],
        "fingerprint": "",
    }
    if not mesh_tr or not cmds.objExists(mesh_tr):
        return sig
    try:
        sig["v"] = int(cmds.polyEvaluate(mesh_tr, vertex=True))
    except Exception:
        pass
    try:
        sig["e"] = int(cmds.polyEvaluate(mesh_tr, edge=True))
    except Exception:
        pass
    try:
        sig["f"] = int(cmds.polyEvaluate(mesh_tr, face=True))
    except Exception:
        pass
    try:
        bb = cmds.exactWorldBoundingBox(mesh_tr) or []
        if len(bb) >= 6:
            b = [float(bb[i]) for i in range(6)]
            sig["bbox"] = b
            sig["size"] = [b[3] - b[0], b[4] - b[1], b[5] - b[2]]
    except Exception:
        pass
    raw = "{0}|{1}|{2}|{3:.6f}|{4:.6f}|{5:.6f}|{6:.6f}|{7:.6f}|{8:.6f}".format(
        sig["v"],
        sig["e"],
        sig["f"],
        float(sig["bbox"][0]),
        float(sig["bbox"][1]),
        float(sig["bbox"][2]),
        float(sig["bbox"][3]),
        float(sig["bbox"][4]),
        float(sig["bbox"][5]),
    )
    sig["fingerprint"] = hashlib.sha1(raw.encode("utf-8")).hexdigest()[:12]
    return sig


def get_skin_signature(mesh_tr):
    sig = {
        "mesh": str(mesh_tr or ""),
        "hasSkin": False,
        "cluster": "",
        "influenceCount": 0,
        "influencePreview": "",
        "normalizeWeights": None,
        "skinningMethod": None,
    }
    if not mesh_tr or not cmds.objExists(mesh_tr):
        return sig
    sc = find_skin_cluster(mesh_tr)
    if not sc:
        return sig
    sig["hasSkin"] = True
    sig["cluster"] = str(sc)
    infs = cmds.skinCluster(sc, q=True, inf=True) or []
    canon = [canonical_influence_name(x) for x in infs]
    sig["influenceCount"] = int(len(canon))
    preview = canon[:8]
    if len(canon) > 8:
        preview.append("...")
    sig["influencePreview"] = ",".join(preview)
    sig["normalizeWeights"] = _safe_get_attr(sc + ".normalizeWeights", None)
    sig["skinningMethod"] = _safe_get_attr(sc + ".skinningMethod", None)
    return sig


def stats_delta(scene_stats, post_stats):
    s = scene_stats or {}
    p = post_stats or {}
    return {
        "countScene": int(s.get("count", 0)),
        "countPost": int(p.get("count", 0)),
        "maxDelta": float(p.get("max", 0.0) - s.get("max", 0.0)),
        "avgDelta": float(p.get("avg", 0.0) - s.get("avg", 0.0)),
        "p95Delta": float(p.get("p95", 0.0) - s.get("p95", 0.0)),
        "snapDelta": float(p.get("snap", 0.0) - s.get("snap", 0.0)),
    }


def format_mesh_sig(sig):
    s = sig or {}
    bb = s.get("bbox", [0.0, 0.0, 0.0, 0.0, 0.0, 0.0])
    sz = s.get("size", [0.0, 0.0, 0.0])
    return "v={0},e={1},f={2},bb=({3:.3f},{4:.3f},{5:.3f})-({6:.3f},{7:.3f},{8:.3f}),size=({9:.3f},{10:.3f},{11:.3f}),fp={12}".format(
        int(s.get("v", 0)),
        int(s.get("e", 0)),
        int(s.get("f", 0)),
        float(bb[0]) if len(bb) > 0 else 0.0,
        float(bb[1]) if len(bb) > 1 else 0.0,
        float(bb[2]) if len(bb) > 2 else 0.0,
        float(bb[3]) if len(bb) > 3 else 0.0,
        float(bb[4]) if len(bb) > 4 else 0.0,
        float(bb[5]) if len(bb) > 5 else 0.0,
        float(sz[0]) if len(sz) > 0 else 0.0,
        float(sz[1]) if len(sz) > 1 else 0.0,
        float(sz[2]) if len(sz) > 2 else 0.0,
        str(s.get("fingerprint", "")),
    )


def format_skin_sig(sig):
    s = sig or {}
    return "hasSkin={0},cluster={1},infs={2},norm={3},method={4},preview=[{5}]".format(
        bool(s.get("hasSkin", False)),
        str(s.get("cluster", "")),
        int(s.get("influenceCount", 0)),
        str(s.get("normalizeWeights", "")),
        str(s.get("skinningMethod", "")),
        str(s.get("influencePreview", "")),
    )


def format_drift(delta):
    d = delta or {}
    return "count(scene={0},post={1}), dMax={2:.6f}, dAvg={3:.6f}, dP95={4:.6f}, dSnap={5:.6f}".format(
        int(d.get("countScene", 0)),
        int(d.get("countPost", 0)),
        float(d.get("maxDelta", 0.0)),
        float(d.get("avgDelta", 0.0)),
        float(d.get("p95Delta", 0.0)),
        float(d.get("snapDelta", 0.0)),
    )


def compare_pair_sets(reference_pairs, observed_pairs):
    # Compare mapping consistency by target vertex index.
    ref = {}
    obs = {}
    for ti, si in reference_pairs or []:
        ref[int(ti)] = int(si)
    for ti, si in observed_pairs or []:
        obs[int(ti)] = int(si)
    common = sorted(set(ref.keys()).intersection(set(obs.keys())))
    mismatch = 0
    match = 0
    for ti in common:
        if ref.get(ti) == obs.get(ti):
            match += 1
        else:
            mismatch += 1
    ref_only = len(set(ref.keys()) - set(obs.keys()))
    obs_only = len(set(obs.keys()) - set(ref.keys()))
    rate = float(match / len(common)) if common else 0.0
    return {
        "refCount": len(ref),
        "obsCount": len(obs),
        "common": len(common),
        "match": match,
        "mismatch": mismatch,
        "refOnly": ref_only,
        "obsOnly": obs_only,
        "matchRate": rate,
    }


def format_pair_cmp(c):
    cc = c or {}
    return "ref={0},obs={1},common={2},match={3},mismatch={4},refOnly={5},obsOnly={6},matchRate={7:.3f}".format(
        int(cc.get("refCount", 0)),
        int(cc.get("obsCount", 0)),
        int(cc.get("common", 0)),
        int(cc.get("match", 0)),
        int(cc.get("mismatch", 0)),
        int(cc.get("refOnly", 0)),
        int(cc.get("obsOnly", 0)),
        float(cc.get("matchRate", 0.0)),
    )


def count_history_node_types(mesh_tr, type_names):
    out = {}
    for t in type_names or []:
        out[str(t)] = 0
    if not mesh_tr or not cmds.objExists(mesh_tr):
        return out
    try:
        hist = cmds.listHistory(mesh_tr, pruneDagObjects=False) or []
    except Exception:
        hist = []
    for n in hist:
        try:
            nt = cmds.nodeType(n)
        except Exception:
            nt = ""
        if nt in out:
            out[nt] = int(out.get(nt, 0)) + 1
    return out


def stabilize_target_before_export(mesh_tr):
    # Bake non-deformer/tweak style edits while keeping deformers,
    # to reduce FBX roundtrip drift on skinned meshes.
    before = count_history_node_types(mesh_tr, ["tweak", "polyNormalPerVertex", "polyMoveVertex"])
    status = "skipped"
    try:
        cmds.bakePartialHistory(mesh_tr, prePostDeformers=True)
        status = "ok"
    except Exception as ex:
        status = "failed({0})".format(str(ex))
    after = count_history_node_types(mesh_tr, ["tweak", "polyNormalPerVertex", "polyMoveVertex"])
    return status, before, after


def cleanup_mesh_components(mesh_tr):
    try:
        cmds.polyClean(
            mesh_tr,
            constructionHistory=False,
            cleanVertices=True,
            cleanEdges=True,
            cleanUVs=False,
            cleanPartialUVMapping=False,
            frozen=False,
        )
        return "ok"
    except Exception as ex:
        return "failed({0})".format(str(ex))


def bake_current_shape_into_base(mesh_tr):
    # Bake current evaluated mesh shape back into base via temporary blendShape.
    if not mesh_tr or not cmds.objExists(mesh_tr):
        return "invalid_mesh"
    dup = None
    bs = None
    try:
        dup = cmds.duplicate(mesh_tr, rr=True)[0]
        bs = cmds.blendShape(dup, mesh_tr, frontOfChain=True)[0]
        w_attr = bs + ".w[0]"
        if cmds.objExists(w_attr):
            cmds.setAttr(w_attr, 1.0)
        cmds.delete(bs)
        bs = None
        cmds.delete(dup)
        dup = None
        return "ok"
    except Exception as ex:
        try:
            if bs and cmds.objExists(bs):
                cmds.delete(bs)
        except Exception:
            pass
        try:
            if dup and cmds.objExists(dup):
                cmds.delete(dup)
        except Exception:
            pass
        return "failed({0})".format(str(ex))


def estimate_roundtrip_pos_gain(src_pos, tgt_post_pos, original_target_pos, mapped_pairs):
    # Estimate how much displacement survives one FBX roundtrip.
    # a ~= |post-orig| / |src-orig| ; gain = 1/a.
    ratios = []
    for ti, si in mapped_pairs or []:
        if ti < 0 or si < 0:
            continue
        if ti >= len(original_target_pos) or ti >= len(tgt_post_pos) or si >= len(src_pos):
            continue
        op = original_target_pos[ti]
        sp = src_pos[si]
        pp = tgt_post_pos[ti]
        ddx = sp[0] - op[0]
        ddy = sp[1] - op[1]
        ddz = sp[2] - op[2]
        d_des = math.sqrt(ddx * ddx + ddy * ddy + ddz * ddz)
        if d_des <= 1e-8:
            continue
        odx = pp[0] - op[0]
        ody = pp[1] - op[1]
        odz = pp[2] - op[2]
        d_obs = math.sqrt(odx * odx + ody * ody + odz * odz)
        ratios.append(float(d_obs / d_des))
    if not ratios:
        return 1.0, 0.0, 0
    ratios.sort()
    mid = ratios[len(ratios) // 2]
    a = max(0.15, min(1.0, float(mid)))
    gain = max(1.0, min(2.0, float(1.0 / a)))
    return gain, float(mid), int(len(ratios))


def get_scene_axis_unit():
    try:
        unit = cmds.currentUnit(q=True, linear=True)
    except Exception:
        unit = "unknown"
    try:
        up_axis = cmds.upAxis(q=True, axis=True)
    except Exception:
        up_axis = "unknown"
    return "unit={0}, upAxis={1}".format(unit, up_axis)


def configure_fbx_export(input_connections=True):
    # Lock down common FBX options to reduce roundtrip drift.
    try:
        mel.eval("FBXResetExport;")
    except Exception:
        pass
    for cmd in [
        "FBXExportSkins -v true;",
        "FBXExportShapes -v true;",
        "FBXExportBakeComplexAnimation -v true;",
        "FBXExportBakeComplexStart -v 1;",
        "FBXExportBakeComplexEnd -v 1;",
        "FBXExportBakeComplexStep -v 1;",
        "FBXExportSmoothMesh -v false;",
        "FBXExportSmoothingGroups -v true;",
        "FBXExportHardEdges -v false;",
        "FBXExportTangents -v false;",
        "FBXExportTriangulate -v false;",
        "FBXExportInputConnections -v {0};".format("true" if input_connections else "false"),
        "FBXExportConstraints -v false;",
        "FBXExportLights -v false;",
        "FBXExportCameras -v false;",
    ]:
        try:
            mel.eval(cmd)
        except Exception:
            pass


def export_fbx(target_path, export_roots, fallback_mesh, input_connections=True):
    cmds.select(clear=True)
    roots = export_roots or []
    if roots:
        for i, r in enumerate(roots):
            try:
                cmds.select(r, r=(i == 0), add=(i != 0))
            except Exception:
                pass
    else:
        cmds.select(fallback_mesh, r=True)

    # When exporting with input connections disabled, explicitly include skin
    # dependencies to preserve skinned output.
    try:
        sc = find_skin_cluster(fallback_mesh)
        if sc and cmds.objExists(sc):
            cmds.select(sc, add=True)
            infs = cmds.skinCluster(sc, q=True, inf=True) or []
            for j in infs:
                if cmds.objExists(j):
                    cmds.select(j, add=True)
    except Exception:
        pass

    configure_fbx_export(input_connections=input_connections)
    safe = target_path.replace("\\", "/")
    mel.eval('FBXExport -f "{0}" -s'.format(safe))


def reattach_skin_and_export_from_geom(donor_fbx_path, geom_fbx_path, output_fbx_path, target_token, target_name):
    try:
        new_scene()
        ensure_fbx_plugin()
        donor_nodes = import_fbx(donor_fbx_path, "donor")
        geom_nodes = import_fbx(geom_fbx_path, "geom")
        geom_roots = top_level_roots_from_nodes(geom_nodes)

        donor_mesh = resolve_mesh(target_token, "donor", target_name) or resolve_mesh(target_token, None, target_name)
        geom_mesh = resolve_mesh(target_token, "geom", target_name) or resolve_mesh(target_token, None, target_name)
        if not donor_mesh or not geom_mesh:
            return "failed(resolve donor/geom mesh)"

        donor_sc = find_skin_cluster(donor_mesh)
        if donor_sc and cmds.objExists(donor_sc):
            infs = cmds.skinCluster(donor_sc, q=True, inf=True) or []
            if infs:
                geom_sc_existing = find_skin_cluster(geom_mesh)
                if geom_sc_existing and cmds.objExists(geom_sc_existing):
                    try:
                        cmds.skinCluster(geom_sc_existing, e=True, unbind=True)
                    except Exception:
                        pass
                try:
                    donor_method = int(_safe_get_attr(donor_sc + ".skinningMethod", 2) or 2)
                except Exception:
                    donor_method = 2
                new_sc = cmds.skinCluster(
                    infs,
                    geom_mesh,
                    toSelectedBones=True,
                    normalizeWeights=1,
                    skinMethod=donor_method,
                )[0]
                cmds.copySkinWeights(
                    sourceSkin=donor_sc,
                    destinationSkin=new_sc,
                    noMirror=True,
                    surfaceAssociation="closestPoint",
                    influenceAssociation=["name", "closestJoint"],
                )

        export_fbx(output_fbx_path, geom_roots, geom_mesh, input_connections=True)
        return "ok"
    except Exception as ex:
        return "failed({0})".format(str(ex))


def map_one(cfg, target_item):
    global _BHV_BASE_WRITE_OK
    global _BHV_BASE_WRITE_FALLBACK
    _BHV_BASE_WRITE_OK = 0
    _BHV_BASE_WRITE_FALLBACK = 0

    source_path = cfg["sourceFbxPath"]
    target_path = target_item["path"]
    source_token = cfg.get("sourceToken", "HEAD")
    target_token = cfg.get("targetToken", "BODY")
    source_name = cfg.get("sourceNodeName")
    target_name = target_item.get("nodeName")
    copy_pos = bool(cfg.get("copyPosition", True))
    copy_nrm = bool(cfg.get("copyNormal", True))
    copy_skin_weights = bool(cfg.get("copySkinWeightsForMapped", False))
    force_zero = bool(cfg.get("forceTargetVerticesToZero", False))
    force_zero_weights = bool(cfg.get("forceTargetSkinWeightsToZero", False))
    write_markers = bool(cfg.get("writeVertexColorMarkers", False))
    force_absurd_weights = bool(cfg.get("forceAbsurdWeightsForMapped", False))
    force_absurd_weights_all = bool(cfg.get("forceAbsurdWeightsForAllVertices", False))
    absurd_weight_bone_name = str(cfg.get("absurdWeightBoneName", "") or "")
    roundtrip_only = bool(cfg.get("roundtripOnly", False))
    threshold_cm = float(cfg.get("snapThresholdCm", 0.0))
    strip_skin = bool(cfg.get("stripSkinBeforeMap", False))
    source_pose_offsets_local = parse_source_pose_offsets(cfg)
    source_global_offset_world = cfg.get("sourceGlobalOffsetWorld", None)
    excluded_source_indices = cfg.get("excludedSourceIndices", []) or []
    excluded_target_indices = cfg.get("excludedTargetIndices", []) or []

    new_scene()
    ensure_fbx_plugin()
    src_nodes = import_fbx(source_path, "src")
    tgt_nodes = import_fbx(target_path, "tgt")
    export_roots = top_level_roots_from_nodes(tgt_nodes)

    src_mesh = resolve_mesh(source_token, "src", source_name) or resolve_mesh(source_token, None, source_name)
    tgt_mesh = resolve_mesh(target_token, "tgt", target_name) or resolve_mesh(target_token, None, target_name)
    if not src_mesh:
        raise RuntimeError(
            "source mesh resolve failed (token='{0}', exact='{1}')".format(
                source_token or "",
                source_name or "",
            )
        )
    if not tgt_mesh:
        raise RuntimeError(
            "target mesh resolve failed (token='{0}', exact='{1}')".format(
                target_token or "",
                target_name or "",
            )
        )

    source_cleanup_status = "disabled"
    target_cleanup_status = "disabled"
    boundary_source_indices = get_boundary_vertex_indices(src_mesh)
    non_isolated_source_indices, source_iso_stat = build_non_isolated_source_indices(get_world_positions(src_mesh))
    allowed_source_indices = set(non_isolated_source_indices or [])
    if boundary_source_indices:
        allowed_source_indices = allowed_source_indices.intersection(boundary_source_indices)
    if not allowed_source_indices:
        allowed_source_indices = set(non_isolated_source_indices or [])

    cleanup_meshes = list_mesh_transforms_under_roots(export_roots)
    marker_cleanup_meshes, marker_cleanup_removed = clear_markers_on_meshes(cleanup_meshes, "BHVMap")
    if marker_cleanup_meshes == 0:
        marker_cleanup_meshes, marker_cleanup_removed = clear_target_namespace_markers("tgt", "BHVMap")
    src_sig_initial = get_mesh_signature(src_mesh)
    tgt_sig_initial = get_mesh_signature(tgt_mesh)
    tgt_skin_initial = get_skin_signature(tgt_mesh)

    if roundtrip_only:
        if threshold_cm <= 0.0:
            threshold_cm = 1e-6
        source_pos = get_world_positions(src_mesh)
        source_pose_offsets_world = local_offsets_to_world_vectors(src_mesh, source_pose_offsets_local)
        source_pos_effective = apply_offsets_to_source_positions(source_pos, source_pose_offsets_world)
        source_pos_effective = apply_global_offset(source_pos_effective, source_global_offset_world)
        target_pos = get_world_positions(tgt_mesh)
        pairs, nearest_max = build_threshold_pairs(
            source_pos_effective,
            target_pos,
            threshold_cm,
            excluded_source_indices,
            excluded_target_indices,
            allowed_source_indices=allowed_source_indices,
        )
        pairs, coincident_added = expand_pairs_to_coincident_targets(
            pairs,
            source_pos_effective,
            target_pos,
            eps=1e-5,
            excluded_target_indices=excluded_target_indices,
        )
        mapped_pairs = [(ti, si) for ti, si, _ in pairs]
        pre_verify = verify_pairs(source_pos_effective, target_pos, mapped_pairs)
        pre_verify_nearest = verify_nearest(source_pos_effective, target_pos, mapped_pairs)

        scene_pairs_used, scene_pair_mode, scene_marker_bad = choose_verify_pairs(
            tgt_mesh,
            len(source_pos_effective),
            mapped_pairs,
            False,
        )
        scene_verify_by_index = verify_pairs(source_pos_effective, target_pos, scene_pairs_used)
        scene_verify_by_nearest = verify_nearest(source_pos_effective, target_pos, scene_pairs_used)

        export_fbx(target_path, export_roots, tgt_mesh)

        new_scene()
        ensure_fbx_plugin()
        import_fbx(source_path, "src")
        import_fbx(target_path, "tgt")
        src_verify = resolve_mesh(source_token, "src", source_name) or resolve_mesh(source_token, None, source_name)
        tgt_verify = resolve_mesh(target_token, "tgt", target_name) or resolve_mesh(target_token, None, target_name)
        if not src_verify or not tgt_verify:
            raise RuntimeError("roundtrip-only verify mesh resolve failed")
        src_verify_pos = apply_offsets_to_source_positions(get_world_positions(src_verify), source_pose_offsets_world)
        src_verify_pos = apply_global_offset(src_verify_pos, source_global_offset_world)
        tgt_verify_pos = get_world_positions(tgt_verify)
        src_sig_post = get_mesh_signature(src_verify)
        tgt_sig_post = get_mesh_signature(tgt_verify)
        tgt_skin_post = get_skin_signature(tgt_verify)
        verify_pairs_used, verify_pair_mode, verify_marker_bad = choose_verify_pairs(
            tgt_verify,
            len(src_verify_pos),
            mapped_pairs,
            False,
        )
        post_by_index = verify_pairs(src_verify_pos, tgt_verify_pos, verify_pairs_used)
        post_by_nearest = verify_nearest(src_verify_pos, tgt_verify_pos, verify_pairs_used)
        post_by_mapped = verify_pairs(src_verify_pos, tgt_verify_pos, mapped_pairs)
        scene_marker_pairs, _scene_bad = decode_marker_pairs(tgt_mesh, len(source_pos_effective))
        post_marker_pairs, _post_bad = decode_marker_pairs(tgt_verify, len(src_verify_pos))
        pair_cmp_scene = compare_pair_sets(mapped_pairs, scene_marker_pairs)
        pair_cmp_post = compare_pair_sets(mapped_pairs, post_marker_pairs)
        drift_index = stats_delta(scene_verify_by_index, post_by_index)
        drift_nearest = stats_delta(scene_verify_by_nearest, post_by_nearest)
        return {
            "mapped": len(pairs),
            "coincidentExpanded": int(coincident_added),
            "total": len(target_pos),
            "moved": 0,
            "movedMax": 0.0,
            "movedAvg": 0.0,
            "mode": "roundtrip_only",
            "sourceCount": len(source_pos),
            "sourceBoundaryCount": len(boundary_source_indices),
            "sourceAllowedCount": len(allowed_source_indices),
            "sourceIsolatedCount": int(source_iso_stat.get("isolated", 0)),
            "adaptiveTh": threshold_cm,
            "nearestMax": nearest_max,
            "roundtripOnly": True,
            "stripSkinBeforeMap": False,
            "sourceSkinStripped": False,
            "targetSkinStripped": False,
            "copySkinWeightsForMapped": False,
            "forceTargetVerticesToZero": False,
            "forceTargetSkinWeightsToZero": False,
            "writeVertexColorMarkers": False,
            "forceAbsurdWeightsForMapped": False,
            "forceAbsurdWeightsForAllVertices": False,
            "absurdWeightBoneUsed": "",
            "weightCopied": 0,
            "weightCopyStatus": "roundtrip_only",
            "zeroWeightVertices": 0,
            "zeroWeightStatus": "roundtrip_only",
            "absurdWeightVertices": 0,
            "absurdWeightStatus": "roundtrip_only",
            "markerTagged": 0,
            "markerUniqueSrc": 0,
            "markerStatus": "roundtrip_only",
            "sourceCleanupStatus": source_cleanup_status,
            "targetCleanupStatus": target_cleanup_status,
            "markerCleanupMeshes": marker_cleanup_meshes,
            "markerCleanupRemovedSets": marker_cleanup_removed,
            "sourcePoseOffsetCount": len(source_pose_offsets_local),
            "markerCorrectionPairs": 0,
            "markerCorrectionBad": 0,
            "markerCorrectionMoved": 0,
            "markerCorrectionWeightCopied": 0,
            "markerCorrectionWeightStatus": "roundtrip_only",
            "envelopeDisableStatus": "roundtrip_only",
            "envelopeDisableCount": 0,
            "envelopeRestoreStatus": "roundtrip_only",
            "envelopeRestoreCount": 0,
            "markerEnvelopeDisableStatus": "roundtrip_only",
            "markerEnvelopeDisableCount": 0,
            "markerEnvelopeRestoreStatus": "roundtrip_only",
            "markerEnvelopeRestoreCount": 0,
            "bindPoseStatus": "roundtrip_only",
            "bindPoseRestoreJoints": 0,
            "preVerify": pre_verify,
            "preVerifyNearest": pre_verify_nearest,
            "postVerifyByIndex": post_by_index,
            "postVerifyByNearest": post_by_nearest,
            "postVerifyByMapped": post_by_mapped,
            "verifyPairMode": verify_pair_mode,
            "verifyPairCount": len(verify_pairs_used),
            "verifyMarkerBad": verify_marker_bad,
            "sceneVerifyByIndex": scene_verify_by_index,
            "sceneVerifyByNearest": scene_verify_by_nearest,
            "sceneVerifyPairMode": scene_pair_mode,
            "sceneVerifyPairCount": len(scene_pairs_used),
            "sceneVerifyMarkerBad": scene_marker_bad,
            "pairCompareSceneMarker": pair_cmp_scene,
            "pairComparePostMarker": pair_cmp_post,
            "sceneToPostDriftByIndex": drift_index,
            "sceneToPostDriftByNearest": drift_nearest,
            "srcMeshSigInitial": src_sig_initial,
            "tgtMeshSigInitial": tgt_sig_initial,
            "tgtSkinSigInitial": tgt_skin_initial,
            "srcMeshSigPost": src_sig_post,
            "tgtMeshSigPost": tgt_sig_post,
            "tgtSkinSigPost": tgt_skin_post,
            "movedDetail": [],
            "srcMesh": src_mesh,
            "tgtMesh": tgt_mesh,
            "io": get_scene_axis_unit(),
            "exportRootCount": len(export_roots),
            "baseWriteOk": _BHV_BASE_WRITE_OK,
            "baseWriteFallback": _BHV_BASE_WRITE_FALLBACK,
        }

    bind_state = capture_joint_world_matrices([src_mesh, tgt_mesh])
    bind_status = set_meshes_to_bind_pose([src_mesh, tgt_mesh])

    stripped_src = False
    stripped_tgt = False
    if strip_skin:
        stripped_src = strip_skin_cluster(src_mesh)
        stripped_tgt = strip_skin_cluster(tgt_mesh)

    env_state = {}
    env_disable_status = "skipped_stripSkin"
    env_restore_status = "skipped_stripSkin"
    env_disable_count = 0
    env_restore_count = 0
    if not strip_skin:
        env_state = capture_skincluster_envelopes([src_mesh, tgt_mesh])
        env_disable_status, env_disable_count = set_skincluster_envelopes(env_state, 0.0)

    source_pos = get_world_positions(src_mesh)
    target_pos = get_world_positions(tgt_mesh)
    source_pose_offsets_world = local_offsets_to_world_vectors(src_mesh, source_pose_offsets_local)
    source_pos_effective = apply_offsets_to_source_positions(source_pos, source_pose_offsets_world)
    source_pos_effective = apply_global_offset(source_pos_effective, source_global_offset_world)
    source_nrm = get_world_normals(src_mesh) if copy_nrm else []

    if not source_pos or not target_pos:
        export_fbx(target_path, export_roots, tgt_mesh)
        return {
            "mapped": 0,
            "total": len(target_pos),
            "moved": 0,
            "movedMax": 0.0,
            "movedAvg": 0.0,
            "mode": "threshold_all_within",
            "sourceCount": len(source_pos),
            "adaptiveTh": threshold_cm,
            "nearestMax": 0.0,
            "roundtripOnly": roundtrip_only,
            "stripSkinBeforeMap": strip_skin,
            "sourceSkinStripped": stripped_src,
            "targetSkinStripped": stripped_tgt,
            "copySkinWeightsForMapped": copy_skin_weights,
            "forceTargetVerticesToZero": force_zero,
            "forceTargetSkinWeightsToZero": force_zero_weights,
            "writeVertexColorMarkers": write_markers,
            "forceAbsurdWeightsForMapped": force_absurd_weights,
            "forceAbsurdWeightsForAllVertices": force_absurd_weights_all,
            "absurdWeightBoneUsed": "",
            "weightCopied": 0,
            "weightCopyStatus": "no_vertices",
            "zeroWeightVertices": 0,
            "zeroWeightStatus": "no_vertices",
            "absurdWeightVertices": 0,
            "absurdWeightStatus": "no_vertices",
            "markerTagged": 0,
            "markerUniqueSrc": 0,
            "markerStatus": "no_vertices",
            "sourceCleanupStatus": source_cleanup_status,
            "targetCleanupStatus": target_cleanup_status,
            "markerCleanupMeshes": marker_cleanup_meshes,
            "markerCleanupRemovedSets": marker_cleanup_removed,
            "sourcePoseOffsetCount": len(source_pose_offsets_local),
            "markerCorrectionPairs": 0,
            "markerCorrectionBad": 0,
            "markerCorrectionMoved": 0,
            "markerCorrectionWeightCopied": 0,
            "markerCorrectionWeightStatus": "disabled",
            "preVerify": {"count": 0, "max": 0.0, "avg": 0.0, "p95": 0.0, "snap": 0.0},
            "postVerifyByIndex": {"count": 0, "max": 0.0, "avg": 0.0, "p95": 0.0, "snap": 0.0},
            "postVerifyByNearest": {"count": 0, "max": 0.0, "avg": 0.0, "p95": 0.0, "snap": 0.0},
            "movedDetail": [],
            "srcMesh": src_mesh,
            "tgtMesh": tgt_mesh,
            "io": get_scene_axis_unit(),
            "exportRootCount": len(export_roots),
            "baseWriteOk": _BHV_BASE_WRITE_OK,
            "baseWriteFallback": _BHV_BASE_WRITE_FALLBACK,
        }

    # 严格语义：阈值=0 仅吸附重合点（或数值误差范围内的点）
    if threshold_cm <= 0.0:
        threshold_cm = 1e-6

    pairs, nearest_max = build_threshold_pairs(
        source_pos_effective,
        target_pos,
        threshold_cm,
        excluded_source_indices,
        excluded_target_indices,
        allowed_source_indices=allowed_source_indices,
    )
    pairs, coincident_added = expand_pairs_to_coincident_targets(
        pairs,
        source_pos_effective,
        target_pos,
        eps=1e-5,
        excluded_target_indices=excluded_target_indices,
    )
    moved = 0
    moved_sum = 0.0
    moved_max = 0.0
    moved_detail = []

    for ti, si, _ in pairs:
        sp = source_pos[si]
        if si < len(source_pos_effective):
            sp = source_pos_effective[si]
        tp = target_pos[ti]
        dx = sp[0] - tp[0]
        dy = sp[1] - tp[1]
        dz = sp[2] - tp[2]
        d = math.sqrt(dx * dx + dy * dy + dz * dz)
        if copy_pos and d > 1e-9:
            set_world_position(tgt_mesh, ti, sp)
        if copy_nrm and si < len(source_nrm):
            set_world_normal(tgt_mesh, ti, source_nrm[si])
        if d > 1e-5:
            moved += 1
            moved_sum += d
            if d > moved_max:
                moved_max = d
            moved_detail.append((ti, si, sp[0], sp[1], sp[2], tp[0], tp[1], tp[2], dx, dy, dz))

    weight_copied = 0
    weight_copy_status = "disabled"
    if copy_skin_weights:
        if strip_skin:
            weight_copy_status = "skipped_stripSkin"
        else:
            weight_copied, weight_copy_status = copy_skin_weights_for_pairs(
                src_mesh,
                tgt_mesh,
                [(ti, si) for ti, si, _ in pairs],
            )

    if force_zero:
        tgt_count = int(cmds.polyEvaluate(tgt_mesh, vertex=True))
        for vi in range(tgt_count):
            set_world_position(tgt_mesh, vi, (0.0, 0.0, 0.0))

    zero_weight_vertices = 0
    zero_weight_status = "disabled"
    if force_zero_weights:
        if strip_skin:
            zero_weight_status = "skipped_stripSkin"
        else:
            zero_weight_vertices, zero_weight_status = zero_skin_weights(tgt_mesh)

    absurd_weight_vertices = 0
    absurd_weight_bone_used = ""
    absurd_weight_status = "disabled"
    if force_absurd_weights_all:
        if strip_skin:
            absurd_weight_status = "skipped_stripSkin"
        else:
            absurd_weight_vertices, absurd_weight_bone_used, absurd_weight_status = force_absurd_weights_for_all_vertices(
                tgt_mesh,
                absurd_weight_bone_name,
            )
    elif force_absurd_weights:
        if strip_skin:
            absurd_weight_status = "skipped_stripSkin"
        else:
            absurd_weight_vertices, absurd_weight_bone_used, absurd_weight_status = force_absurd_weights_for_pairs(
                tgt_mesh,
                pairs,
                absurd_weight_bone_name,
            )

    marker_tagged = 0
    marker_unique = 0
    marker_status = "disabled"
    if write_markers:
        marker_tagged, marker_unique, marker_status = apply_vertex_markers(tgt_mesh, pairs)

    final_target_pos = get_world_positions(tgt_mesh)
    mapped_pairs = [(ti, si) for ti, si, _ in pairs]
    mapped_self_pairs = [(ti, ti) for ti, _si in mapped_pairs if 0 <= ti < len(target_pos)]
    pre_verify = verify_pairs(source_pos_effective, final_target_pos, mapped_pairs)

    env_restore_vs_map = {"count": 0, "max": 0.0, "avg": 0.0, "p95": 0.0, "snap": 0.0}
    bind_restore_vs_env = {"count": 0, "max": 0.0, "avg": 0.0, "p95": 0.0, "snap": 0.0}
    scene_vs_map = {"count": 0, "max": 0.0, "avg": 0.0, "p95": 0.0, "snap": 0.0}
    restore_comp = {
        "count": 0,
        "solved": 0,
        "solveRate": 0.0,
        "avgIter": 0.0,
        "avgErr": 0.0,
        "maxErr": 0.0,
        "verifyAfter": {"count": 0, "max": 0.0, "avg": 0.0, "p95": 0.0, "snap": 0.0},
        "tol": 0.0,
        "maxIter": 0,
    }
    env_restored_target_pos = list(final_target_pos)

    if not strip_skin:
        env_restore_status, env_restore_count = restore_skincluster_envelopes(env_state)
        env_restored_target_pos = get_world_positions(tgt_mesh)
        env_restore_vs_map = verify_pairs(final_target_pos, env_restored_target_pos, mapped_self_pairs)

    # Restore joints before verification/export so output FBX keeps original pose space.
    restored_joints = restore_joint_world_matrices(bind_state)
    scene_target_after_restore = get_world_positions(tgt_mesh)
    scene_source_after_restore = apply_offsets_to_source_positions(get_world_positions(src_mesh), source_pose_offsets_world)
    scene_source_after_restore = apply_global_offset(scene_source_after_restore, source_global_offset_world)
    bind_restore_vs_env = verify_pairs(env_restored_target_pos, scene_target_after_restore, mapped_self_pairs)
    scene_vs_map = verify_pairs(final_target_pos, scene_target_after_restore, mapped_self_pairs)
    if (not strip_skin) and mapped_pairs:
        restore_comp = compensate_pairs_after_restore(
            src_mesh,
            tgt_mesh,
            mapped_pairs,
            source_pos_override=scene_source_after_restore,
            max_iter=12,
            tol=1e-5,
        )
        scene_target_after_restore = get_world_positions(tgt_mesh)
        scene_vs_map = verify_pairs(final_target_pos, scene_target_after_restore, mapped_self_pairs)

    # Verify in current Maya scene after envelope/bind restores but before FBX export.
    scene_src_pos = apply_offsets_to_source_positions(get_world_positions(src_mesh), source_pose_offsets_world)
    scene_src_pos = apply_global_offset(scene_src_pos, source_global_offset_world)
    scene_tgt_pos = scene_target_after_restore
    scene_pairs_used, scene_pair_mode, scene_marker_bad = choose_verify_pairs(
        tgt_mesh,
        len(scene_src_pos),
        mapped_pairs,
        write_markers,
    )
    scene_verify_by_index = verify_pairs(scene_src_pos, scene_tgt_pos, scene_pairs_used)
    scene_verify_by_nearest = verify_nearest(scene_src_pos, scene_tgt_pos, scene_pairs_used)
    scene_vs_original = verify_pairs(target_pos, scene_tgt_pos, mapped_self_pairs)
    scene_marker_pairs, _scene_bad = decode_marker_pairs(tgt_mesh, len(scene_src_pos))
    pair_cmp_scene = compare_pair_sets(mapped_pairs, scene_marker_pairs)
    stabilize_status_main, stabilize_before_main, stabilize_after_main = stabilize_target_before_export(tgt_mesh)
    bake_shape_status_main = "disabled"
    tgt_sig_pre_export = get_mesh_signature(tgt_mesh)
    tgt_skin_pre_export = get_skin_signature(tgt_mesh)

    export_fbx(target_path, export_roots, tgt_mesh)

    new_scene()
    ensure_fbx_plugin()
    src_verify_nodes = import_fbx(source_path, "src")
    tgt_verify_nodes = import_fbx(target_path, "tgt")
    verify_export_roots = top_level_roots_from_nodes(tgt_verify_nodes)
    src_verify = resolve_mesh(source_token, "src", source_name) or resolve_mesh(source_token, None, source_name)
    tgt_verify = resolve_mesh(target_token, "tgt", target_name) or resolve_mesh(target_token, None, target_name)
    if not src_verify or not tgt_verify:
        raise RuntimeError("verify mesh resolve failed")
    if strip_skin:
        # Keep verify stage in the same "no skin" space as mapping stage.
        strip_skin_cluster(src_verify)
        strip_skin_cluster(tgt_verify)
    src_verify_pos = apply_offsets_to_source_positions(get_world_positions(src_verify), source_pose_offsets_world)
    src_verify_pos = apply_global_offset(src_verify_pos, source_global_offset_world)
    tgt_verify_pos = get_world_positions(tgt_verify)
    verify_pairs_used, verify_pair_mode, verify_marker_bad = choose_verify_pairs(
        tgt_verify,
        len(src_verify_pos),
        mapped_pairs,
        write_markers,
    )
    post_by_index = verify_pairs(src_verify_pos, tgt_verify_pos, verify_pairs_used)
    post_by_nearest = verify_nearest(src_verify_pos, tgt_verify_pos, verify_pairs_used)
    post_by_mapped = verify_pairs(src_verify_pos, tgt_verify_pos, mapped_pairs)
    post_vs_original = verify_pairs(target_pos, tgt_verify_pos, mapped_self_pairs)
    post_marker_pairs, _post_bad = decode_marker_pairs(tgt_verify, len(src_verify_pos))
    pair_cmp_post = compare_pair_sets(mapped_pairs, post_marker_pairs)
    post_src_mesh = src_verify
    post_tgt_mesh = tgt_verify

    marker_corr = {
        "markerPairs": 0,
        "markerBad": 0,
        "markerMoved": 0,
        "markerWeightCopied": 0,
        "markerWeightStatus": "disabled",
    }
    marker_env_disable_status = "disabled"
    marker_env_restore_status = "disabled"
    marker_env_disable_count = 0
    marker_env_restore_count = 0
    marker_export_with_envelope_zero = False
    stabilize_status_marker = "not_run"
    stabilize_before_marker = {}
    stabilize_after_marker = {}
    bake_shape_status_marker = "not_run"
    skinReattachStatus = "disabled"
    marker_pos_gain = 1.0
    marker_gain_ratio_median = 0.0
    marker_gain_samples = 0
    if write_markers:
        _gain_suggest, marker_gain_ratio_median, marker_gain_samples = estimate_roundtrip_pos_gain(
            src_verify_pos,
            tgt_verify_pos,
            target_pos,
            mapped_pairs,
        )
        # Apply estimated roundtrip compensation by default.
        # Keep an upper cap to avoid over-shoot on unstable samples.
        marker_pos_gain = max(1.0, min(2.0, float(_gain_suggest)))
        marker_env_state = {}
        if not strip_skin:
            marker_env_state = capture_skincluster_envelopes([src_verify, tgt_verify])
            marker_env_disable_status, marker_env_disable_count = set_skincluster_envelopes(marker_env_state, 0.0)
        marker_corr = apply_marker_driven_correction(
            src_verify,
            tgt_verify,
            copy_pos,
            copy_nrm,
            copy_skin_weights,
            marker_pos_gain,
            target_pos,
        )
        if not strip_skin:
            # Keep envelopes disabled for the marker-correction export pass.
            # Restoring before export can pull corrected points back to baseline.
            marker_export_with_envelope_zero = True
            marker_env_restore_status = "deferred_until_after_export"
        # marker correction stage runs in a verify scene; export full tgt hierarchy from this scene
        stabilize_status_marker, stabilize_before_marker, stabilize_after_marker = stabilize_target_before_export(tgt_verify)
        bake_shape_status_marker = "disabled"
        export_fbx(target_path, verify_export_roots, tgt_verify)
        if not strip_skin and marker_export_with_envelope_zero:
            marker_env_restore_status, marker_env_restore_count = restore_skincluster_envelopes(marker_env_state)
        export_fbx(target_path, verify_export_roots, tgt_verify, input_connections=True)

        new_scene()
        ensure_fbx_plugin()
        import_fbx(source_path, "src")
        import_fbx(target_path, "tgt")
        src_verify2 = resolve_mesh(source_token, "src", source_name) or resolve_mesh(source_token, None, source_name)
        tgt_verify2 = resolve_mesh(target_token, "tgt", target_name) or resolve_mesh(target_token, None, target_name)
        if not src_verify2 or not tgt_verify2:
            raise RuntimeError("marker-correction verify mesh resolve failed")
        if strip_skin:
            strip_skin_cluster(src_verify2)
            strip_skin_cluster(tgt_verify2)
        src_verify_pos = apply_offsets_to_source_positions(get_world_positions(src_verify2), source_pose_offsets_world)
        src_verify_pos = apply_global_offset(src_verify_pos, source_global_offset_world)
        tgt_verify_pos = get_world_positions(tgt_verify2)
        verify_pairs_used, verify_pair_mode, verify_marker_bad = choose_verify_pairs(
            tgt_verify2,
            len(src_verify_pos),
            mapped_pairs,
            True,
        )
        post_by_index = verify_pairs(src_verify_pos, tgt_verify_pos, verify_pairs_used)
        post_by_nearest = verify_nearest(src_verify_pos, tgt_verify_pos, verify_pairs_used)
        post_by_mapped = verify_pairs(src_verify_pos, tgt_verify_pos, mapped_pairs)
        post_vs_original = verify_pairs(target_pos, tgt_verify_pos, mapped_self_pairs)
        post_marker_pairs, _post_bad = decode_marker_pairs(tgt_verify2, len(src_verify_pos))
        pair_cmp_post = compare_pair_sets(mapped_pairs, post_marker_pairs)
        post_src_mesh = src_verify2
        post_tgt_mesh = tgt_verify2

    moved_avg = (moved_sum / moved) if moved > 0 else 0.0
    src_sig_post = get_mesh_signature(post_src_mesh)
    tgt_sig_post = get_mesh_signature(post_tgt_mesh)
    tgt_skin_post = get_skin_signature(post_tgt_mesh)
    drift_index = stats_delta(scene_verify_by_index, post_by_index)
    drift_nearest = stats_delta(scene_verify_by_nearest, post_by_nearest)
    return {
        "mapped": len(pairs),
        "coincidentExpanded": int(coincident_added),
        "total": len(target_pos),
        "moved": moved,
        "movedMax": moved_max,
        "movedAvg": moved_avg,
        "mode": "threshold_all_within",
        "sourceCount": len(source_pos),
        "sourceBoundaryCount": len(boundary_source_indices),
        "sourceAllowedCount": len(allowed_source_indices),
        "sourceIsolatedCount": int(source_iso_stat.get("isolated", 0)),
        "adaptiveTh": threshold_cm,
        "nearestMax": nearest_max,
        "roundtripOnly": roundtrip_only,
        "stripSkinBeforeMap": strip_skin,
        "sourceSkinStripped": stripped_src,
        "targetSkinStripped": stripped_tgt,
        "copySkinWeightsForMapped": copy_skin_weights,
        "forceTargetVerticesToZero": force_zero,
        "forceTargetSkinWeightsToZero": force_zero_weights,
        "writeVertexColorMarkers": write_markers,
        "forceAbsurdWeightsForMapped": force_absurd_weights,
        "forceAbsurdWeightsForAllVertices": force_absurd_weights_all,
        "absurdWeightBoneUsed": absurd_weight_bone_used,
        "weightCopied": weight_copied,
        "weightCopyStatus": weight_copy_status,
        "zeroWeightVertices": zero_weight_vertices,
        "zeroWeightStatus": zero_weight_status,
        "absurdWeightVertices": absurd_weight_vertices,
        "absurdWeightStatus": absurd_weight_status,
        "markerTagged": marker_tagged,
        "markerUniqueSrc": marker_unique,
        "markerStatus": marker_status,
        "sourceCleanupStatus": source_cleanup_status,
        "targetCleanupStatus": target_cleanup_status,
        "markerCleanupMeshes": marker_cleanup_meshes,
        "markerCleanupRemovedSets": marker_cleanup_removed,
        "sourcePoseOffsetCount": len(source_pose_offsets_local),
        "markerCorrectionPairs": int(marker_corr.get("markerPairs", 0)),
        "markerCorrectionBad": int(marker_corr.get("markerBad", 0)),
        "markerCorrectionMoved": int(marker_corr.get("markerMoved", 0)),
        "markerCorrectionWeightCopied": int(marker_corr.get("markerWeightCopied", 0)),
        "markerCorrectionWeightStatus": str(marker_corr.get("markerWeightStatus", "disabled")),
        "markerCorrectionPosGain": float(marker_corr.get("markerPosGain", marker_pos_gain)),
        "markerGainRatioMedian": marker_gain_ratio_median,
        "markerGainSamples": marker_gain_samples,
        "envelopeDisableStatus": env_disable_status,
        "envelopeDisableCount": env_disable_count,
        "envelopeRestoreStatus": env_restore_status,
        "envelopeRestoreCount": env_restore_count,
        "markerEnvelopeDisableStatus": marker_env_disable_status,
        "markerEnvelopeDisableCount": marker_env_disable_count,
        "markerEnvelopeRestoreStatus": marker_env_restore_status,
        "markerEnvelopeRestoreCount": marker_env_restore_count,
        "markerExportWithEnvelopeZero": marker_export_with_envelope_zero,
        "bindPoseStatus": bind_status,
        "bindPoseRestoreJoints": restored_joints,
        "preVerify": pre_verify,
        "postVerifyByIndex": post_by_index,
        "postVerifyByNearest": post_by_nearest,
        "postVerifyByMapped": post_by_mapped,
        "verifyPairMode": verify_pair_mode,
        "verifyPairCount": len(verify_pairs_used),
        "verifyMarkerBad": verify_marker_bad,
        "sceneVerifyByIndex": scene_verify_by_index,
        "sceneVerifyByNearest": scene_verify_by_nearest,
        "sceneVerifyPairMode": scene_pair_mode,
        "sceneVerifyPairCount": len(scene_pairs_used),
        "sceneVerifyMarkerBad": scene_marker_bad,
        "sceneVsOriginalByMappedTarget": scene_vs_original,
        "envRestoreVsMappedTarget": env_restore_vs_map,
        "bindRestoreVsEnvelopeTarget": bind_restore_vs_env,
        "sceneVsMappedTarget": scene_vs_map,
        "restoreCompensation": restore_comp,
        "postVsOriginalByMappedTarget": post_vs_original,
        "pairCompareSceneMarker": pair_cmp_scene,
        "pairComparePostMarker": pair_cmp_post,
        "stabilizeStatusMain": stabilize_status_main,
        "stabilizeBeforeMain": stabilize_before_main,
        "stabilizeAfterMain": stabilize_after_main,
        "bakeShapeStatusMain": bake_shape_status_main,
        "stabilizeStatusMarker": stabilize_status_marker,
        "stabilizeBeforeMarker": stabilize_before_marker,
        "stabilizeAfterMarker": stabilize_after_marker,
        "bakeShapeStatusMarker": bake_shape_status_marker,
        "skinReattachStatus": skinReattachStatus,
        "sceneToPostDriftByIndex": drift_index,
        "sceneToPostDriftByNearest": drift_nearest,
        "srcMeshSigInitial": src_sig_initial,
        "tgtMeshSigInitial": tgt_sig_initial,
        "tgtSkinSigInitial": tgt_skin_initial,
        "tgtMeshSigPreExport": tgt_sig_pre_export,
        "tgtSkinSigPreExport": tgt_skin_pre_export,
        "srcMeshSigPost": src_sig_post,
        "tgtMeshSigPost": tgt_sig_post,
        "tgtSkinSigPost": tgt_skin_post,
        "movedDetail": moved_detail,
        "srcMesh": src_mesh,
        "tgtMesh": tgt_mesh,
        "io": get_scene_axis_unit(),
        "exportRootCount": len(export_roots),
        "baseWriteOk": _BHV_BASE_WRITE_OK,
        "baseWriteFallback": _BHV_BASE_WRITE_FALLBACK,
    }


def main():
    config_path, result_path = get_cli_paths()
    with io.open(config_path, "r", encoding="utf-8-sig") as f:
        cfg = json.load(f)

    lines = []
    targets = cfg.get("targets", [])
    sgow = parse_vec3(cfg.get("sourceGlobalOffsetWorld", None), (0.0, 0.0, 0.0))
    lines.append(
        "config: snapThresholdCm={0:.6f}, stripSkinBeforeMap={1}, copySkinWeightsForMapped={2}, forceTargetVerticesToZero={3}, forceTargetSkinWeightsToZero={4}, writeVertexColorMarkers={5}, forceAbsurdWeightsForMapped={6}, forceAbsurdWeightsForAllVertices={7}, absurdWeightBoneName={8}, roundtripOnly={9}, sourcePoseOffsets={10}, sourceGlobalOffset=({11:.6f},{12:.6f},{13:.6f})".format(
            float(cfg.get("snapThresholdCm", 0.0)),
            bool(cfg.get("stripSkinBeforeMap", False)),
            bool(cfg.get("copySkinWeightsForMapped", False)),
            bool(cfg.get("forceTargetVerticesToZero", False)),
            bool(cfg.get("forceTargetSkinWeightsToZero", False)),
            bool(cfg.get("writeVertexColorMarkers", False)),
            bool(cfg.get("forceAbsurdWeightsForMapped", False)),
            bool(cfg.get("forceAbsurdWeightsForAllVertices", False)),
            str(cfg.get("absurdWeightBoneName", "") or ""),
            bool(cfg.get("roundtripOnly", False)),
            len(cfg.get("sourcePoseOffsetIndices", []) or []),
            float(sgow[0]),
            float(sgow[1]),
            float(sgow[2]),
        )
    )
    for target in targets:
        target_path = target.get("path")
        base_name = os.path.basename(target_path or "")
        try:
            stat = map_one(cfg, target)
            lines.append(
                "{0}: mapped {1}/{2}, moved {3}, moveMax {4:.6f}, moveAvg {5:.6f}, mode={6}, sourceCount={7}, sourceBoundaryCount={8}, sourceAllowedCount={9}, sourceIsolatedCount={10}, threshold={11:.6f}cm, nearestMax={12:.6f}cm, coincidentExpanded={13}".format(
                    base_name,
                    int(stat.get("mapped", 0)),
                    int(stat.get("total", 0)),
                    int(stat.get("moved", 0)),
                    float(stat.get("movedMax", 0.0)),
                    float(stat.get("movedAvg", 0.0)),
                    stat.get("mode", "threshold_all_within"),
                    int(stat.get("sourceCount", 0)),
                    int(stat.get("sourceBoundaryCount", 0)),
                    int(stat.get("sourceAllowedCount", 0)),
                    int(stat.get("sourceIsolatedCount", 0)),
                    float(stat.get("adaptiveTh", 0.0)),
                    float(stat.get("nearestMax", 0.0)),
                    int(stat.get("coincidentExpanded", 0)),
                )
            )
            moved_detail = stat.get("movedDetail", [])
            if moved_detail:
                lines.append("  movedDetail (tgtIdx,srcIdx | src -> tgt_before | delta):")
                for rec in moved_detail:
                    lines.append(
                        "    {0},{1} | ({2:.6f},{3:.6f},{4:.6f}) -> ({5:.6f},{6:.6f},{7:.6f}) | d=({8:.6f},{9:.6f},{10:.6f})".format(
                            int(rec[0]),
                            int(rec[1]),
                            float(rec[2]),
                            float(rec[3]),
                            float(rec[4]),
                            float(rec[5]),
                            float(rec[6]),
                            float(rec[7]),
                            float(rec[8]),
                            float(rec[9]),
                            float(rec[10]),
                        )
                    )
            lines.append("{0}: io {1}".format(base_name, stat.get("io", "unit=unknown, upAxis=unknown")))
            lines.append("{0}: meshes src={1} tgt={2}".format(base_name, stat.get("srcMesh", "?"), stat.get("tgtMesh", "?")))
            lines.append("{0}: exportRootCount={1}".format(base_name, int(stat.get("exportRootCount", 0))))
            lines.append(
                "{0}: baseWrite ok={1}, fallback={2}".format(
                    base_name,
                    int(stat.get("baseWriteOk", 0)),
                    int(stat.get("baseWriteFallback", 0)),
                )
            )
            lines.append(
                "{0}: cleanup src={1}, tgt={2}".format(
                    base_name,
                    str(stat.get("sourceCleanupStatus", "")),
                    str(stat.get("targetCleanupStatus", "")),
                )
            )
            lines.append("{0}: meshSig srcInitial {1}".format(base_name, format_mesh_sig(stat.get("srcMeshSigInitial", {}))))
            lines.append("{0}: meshSig tgtInitial {1}".format(base_name, format_mesh_sig(stat.get("tgtMeshSigInitial", {}))))
            if stat.get("tgtMeshSigPreExport", None) is not None:
                lines.append("{0}: meshSig tgtPreExport {1}".format(base_name, format_mesh_sig(stat.get("tgtMeshSigPreExport", {}))))
            lines.append("{0}: meshSig srcPost {1}".format(base_name, format_mesh_sig(stat.get("srcMeshSigPost", {}))))
            lines.append("{0}: meshSig tgtPost {1}".format(base_name, format_mesh_sig(stat.get("tgtMeshSigPost", {}))))
            lines.append("{0}: skinSig tgtInitial {1}".format(base_name, format_skin_sig(stat.get("tgtSkinSigInitial", {}))))
            if stat.get("tgtSkinSigPreExport", None) is not None:
                lines.append("{0}: skinSig tgtPreExport {1}".format(base_name, format_skin_sig(stat.get("tgtSkinSigPreExport", {}))))
            lines.append("{0}: skinSig tgtPost {1}".format(base_name, format_skin_sig(stat.get("tgtSkinSigPost", {}))))
            lines.append(
                "{0}: stripSkinBeforeMap={1}, sourceSkinStripped={2}, targetSkinStripped={3}".format(
                    base_name,
                    bool(stat.get("stripSkinBeforeMap", False)),
                    bool(stat.get("sourceSkinStripped", False)),
                    bool(stat.get("targetSkinStripped", False)),
                )
            )
            lines.append(
                "{0}: roundtripOnly={1}".format(
                    base_name,
                    bool(stat.get("roundtripOnly", False)),
                )
            )
            lines.append(
                "{0}: weightCopy enabled={1}, copied={2}, status={3}".format(
                    base_name,
                    bool(stat.get("copySkinWeightsForMapped", False)),
                    int(stat.get("weightCopied", 0)),
                    str(stat.get("weightCopyStatus", "disabled")),
                )
            )
            lines.append(
                "{0}: forceTargetVerticesToZero={1}".format(
                    base_name,
                    bool(stat.get("forceTargetVerticesToZero", False)),
                )
            )
            lines.append(
                "{0}: forceTargetSkinWeightsToZero={1}, zeroWeightVertices={2}, status={3}".format(
                    base_name,
                    bool(stat.get("forceTargetSkinWeightsToZero", False)),
                    int(stat.get("zeroWeightVertices", 0)),
                    str(stat.get("zeroWeightStatus", "disabled")),
                )
            )
            lines.append(
                "{0}: forceAbsurdWeightsForMapped={1}, forceAbsurdWeightsForAllVertices={2}, absurdWeightVertices={3}, boneUsed={4}, status={5}".format(
                    base_name,
                    bool(stat.get("forceAbsurdWeightsForMapped", False)),
                    bool(stat.get("forceAbsurdWeightsForAllVertices", False)),
                    int(stat.get("absurdWeightVertices", 0)),
                    str(stat.get("absurdWeightBoneUsed", "")),
                    str(stat.get("absurdWeightStatus", "disabled")),
                )
            )
            lines.append(
                "{0}: marker enabled={1}, tagged={2}, uniqueSrc={3}, status={4}".format(
                    base_name,
                    bool(stat.get("writeVertexColorMarkers", False)),
                    int(stat.get("markerTagged", 0)),
                    int(stat.get("markerUniqueSrc", 0)),
                    str(stat.get("markerStatus", "disabled")),
                )
            )
            lines.append(
                "{0}: markerCleanup meshes={1}, removedSets={2}".format(
                    base_name,
                    int(stat.get("markerCleanupMeshes", 0)),
                    int(stat.get("markerCleanupRemovedSets", 0)),
                )
            )
            lines.append(
                "{0}: sourcePoseOffsets count={1}".format(
                    base_name,
                    int(stat.get("sourcePoseOffsetCount", 0)),
                )
            )
            lines.append(
                "{0}: markerCorrection pairs={1}, bad={2}, moved={3}, weightCopied={4}, weightStatus={5}".format(
                    base_name,
                    int(stat.get("markerCorrectionPairs", 0)),
                    int(stat.get("markerCorrectionBad", 0)),
                    int(stat.get("markerCorrectionMoved", 0)),
                    int(stat.get("markerCorrectionWeightCopied", 0)),
                    str(stat.get("markerCorrectionWeightStatus", "disabled")),
                )
            )
            lines.append(
                "{0}: markerGain posGain={1:.3f}, ratioMedian={2:.6f}, samples={3}".format(
                    base_name,
                    float(stat.get("markerCorrectionPosGain", 1.0)),
                    float(stat.get("markerGainRatioMedian", 0.0)),
                    int(stat.get("markerGainSamples", 0)),
                )
            )
            lines.append(
                "{0}: verifyPairs mode={1}, count={2}, markerBad={3}".format(
                    base_name,
                    str(stat.get("verifyPairMode", "original")),
                    int(stat.get("verifyPairCount", 0)),
                    int(stat.get("verifyMarkerBad", 0)),
                )
            )
            lines.append(
                "{0}: sceneVerify mode={1}, count={2}, markerBad={3}".format(
                    base_name,
                    str(stat.get("sceneVerifyPairMode", "original")),
                    int(stat.get("sceneVerifyPairCount", 0)),
                    int(stat.get("sceneVerifyMarkerBad", 0)),
                )
            )
            lines.append(
                "{0}: pairCmp sceneMarker {1}".format(
                    base_name,
                    format_pair_cmp(stat.get("pairCompareSceneMarker", {})),
                )
            )
            lines.append(
                "{0}: pairCmp postMarker {1}".format(
                    base_name,
                    format_pair_cmp(stat.get("pairComparePostMarker", {})),
                )
            )
            lines.append(
                "{0}: stabilize main status={1}, before={2}, after={3}".format(
                    base_name,
                    str(stat.get("stabilizeStatusMain", "")),
                    str(stat.get("stabilizeBeforeMain", {})),
                    str(stat.get("stabilizeAfterMain", {})),
                )
            )
            lines.append(
                "{0}: bakeShape main status={1}".format(
                    base_name,
                    str(stat.get("bakeShapeStatusMain", "")),
                )
            )
            lines.append(
                "{0}: stabilize marker status={1}, before={2}, after={3}".format(
                    base_name,
                    str(stat.get("stabilizeStatusMarker", "")),
                    str(stat.get("stabilizeBeforeMarker", {})),
                    str(stat.get("stabilizeAfterMarker", {})),
                )
            )
            lines.append(
                "{0}: bakeShape marker status={1}".format(
                    base_name,
                    str(stat.get("bakeShapeStatusMarker", "")),
                )
            )
            lines.append(
                "{0}: skinReattach status={1}".format(
                    base_name,
                    str(stat.get("skinReattachStatus", "")),
                )
            )
            lines.append(
                "{0}: scene->post drift(byIndex) {1}".format(
                    base_name,
                    format_drift(stat.get("sceneToPostDriftByIndex", {})),
                )
            )
            lines.append(
                "{0}: scene->post drift(byNearest) {1}".format(
                    base_name,
                    format_drift(stat.get("sceneToPostDriftByNearest", {})),
                )
            )
            lines.append(
                "{0}: bindPose status={1}, restoreJoints={2}".format(
                    base_name,
                    str(stat.get("bindPoseStatus", "disabled")),
                    int(stat.get("bindPoseRestoreJoints", 0)),
                )
            )
            lines.append(
                "{0}: envelope main disable={1}, restore={2}".format(
                    base_name,
                    str(stat.get("envelopeDisableStatus", "disabled")),
                    str(stat.get("envelopeRestoreStatus", "disabled")),
                )
            )
            lines.append(
                "{0}: envelope marker disable={1}, restore={2}".format(
                    base_name,
                    str(stat.get("markerEnvelopeDisableStatus", "disabled")),
                    str(stat.get("markerEnvelopeRestoreStatus", "disabled")),
                )
            )

            pre_v = stat.get("preVerify", {})
            lines.append(
                "{0}: preVerify mapped={1}, max={2:.6f}, avg={3:.6f}, p95={4:.6f}, snap<=1e-6={5:.3f}".format(
                    base_name,
                    int(pre_v.get("count", 0)),
                    float(pre_v.get("max", 0.0)),
                    float(pre_v.get("avg", 0.0)),
                    float(pre_v.get("p95", 0.0)),
                    float(pre_v.get("snap", 0.0)),
                )
            )
            by_i = stat.get("postVerifyByIndex", {})
            lines.append(
                "{0}: postVerify(byIndex) mapped={1}, max={2:.6f}, avg={3:.6f}, p95={4:.6f}, snap<=1e-6={5:.3f}".format(
                    base_name,
                    int(by_i.get("count", 0)),
                    float(by_i.get("max", 0.0)),
                    float(by_i.get("avg", 0.0)),
                    float(by_i.get("p95", 0.0)),
                    float(by_i.get("snap", 0.0)),
                )
            )
            by_m = stat.get("postVerifyByMapped", {})
            lines.append(
                "{0}: postVerify(byMapped) mapped={1}, max={2:.6f}, avg={3:.6f}, p95={4:.6f}, snap<=1e-6={5:.3f}".format(
                    base_name,
                    int(by_m.get("count", 0)),
                    float(by_m.get("max", 0.0)),
                    float(by_m.get("avg", 0.0)),
                    float(by_m.get("p95", 0.0)),
                    float(by_m.get("snap", 0.0)),
                )
            )
            by_n = stat.get("postVerifyByNearest", {})
            lines.append(
                "{0}: postVerify(byNearest) mapped={1}, max={2:.6f}, avg={3:.6f}, p95={4:.6f}, snap<=1e-6={5:.3f}".format(
                    base_name,
                    int(by_n.get("count", 0)),
                    float(by_n.get("max", 0.0)),
                    float(by_n.get("avg", 0.0)),
                    float(by_n.get("p95", 0.0)),
                    float(by_n.get("snap", 0.0)),
                )
            )
            sc_i = stat.get("sceneVerifyByIndex", {})
            lines.append(
                "{0}: sceneVerify(byIndex) mapped={1}, max={2:.6f}, avg={3:.6f}, p95={4:.6f}, snap<=1e-6={5:.3f}".format(
                    base_name,
                    int(sc_i.get("count", 0)),
                    float(sc_i.get("max", 0.0)),
                    float(sc_i.get("avg", 0.0)),
                    float(sc_i.get("p95", 0.0)),
                    float(sc_i.get("snap", 0.0)),
                )
            )
            sc_n = stat.get("sceneVerifyByNearest", {})
            lines.append(
                "{0}: sceneVerify(byNearest) mapped={1}, max={2:.6f}, avg={3:.6f}, p95={4:.6f}, snap<=1e-6={5:.3f}".format(
                    base_name,
                    int(sc_n.get("count", 0)),
                    float(sc_n.get("max", 0.0)),
                    float(sc_n.get("avg", 0.0)),
                    float(sc_n.get("p95", 0.0)),
                    float(sc_n.get("snap", 0.0)),
                )
            )
            svo = stat.get("sceneVsOriginalByMappedTarget", {})
            lines.append(
                "{0}: sceneVsOriginal(mappedTarget) count={1}, max={2:.6f}, avg={3:.6f}, p95={4:.6f}, snap<=1e-6={5:.3f}".format(
                    base_name,
                    int(svo.get("count", 0)),
                    float(svo.get("max", 0.0)),
                    float(svo.get("avg", 0.0)),
                    float(svo.get("p95", 0.0)),
                    float(svo.get("snap", 0.0)),
                )
            )
            evm = stat.get("envRestoreVsMappedTarget", {})
            lines.append(
                "{0}: envRestoreVsMapped(mappedTarget) count={1}, max={2:.6f}, avg={3:.6f}, p95={4:.6f}, snap<=1e-6={5:.3f}".format(
                    base_name,
                    int(evm.get("count", 0)),
                    float(evm.get("max", 0.0)),
                    float(evm.get("avg", 0.0)),
                    float(evm.get("p95", 0.0)),
                    float(evm.get("snap", 0.0)),
                )
            )
            bve = stat.get("bindRestoreVsEnvelopeTarget", {})
            lines.append(
                "{0}: bindRestoreVsEnvelope(mappedTarget) count={1}, max={2:.6f}, avg={3:.6f}, p95={4:.6f}, snap<=1e-6={5:.3f}".format(
                    base_name,
                    int(bve.get("count", 0)),
                    float(bve.get("max", 0.0)),
                    float(bve.get("avg", 0.0)),
                    float(bve.get("p95", 0.0)),
                    float(bve.get("snap", 0.0)),
                )
            )
            svm = stat.get("sceneVsMappedTarget", {})
            lines.append(
                "{0}: sceneVsMapped(mappedTarget) count={1}, max={2:.6f}, avg={3:.6f}, p95={4:.6f}, snap<=1e-6={5:.3f}".format(
                    base_name,
                    int(svm.get("count", 0)),
                    float(svm.get("max", 0.0)),
                    float(svm.get("avg", 0.0)),
                    float(svm.get("p95", 0.0)),
                    float(svm.get("snap", 0.0)),
                )
            )
            rc = stat.get("restoreCompensation", {})
            rc_after = rc.get("verifyAfter", {})
            lines.append(
                "{0}: restoreComp count={1}, solved={2}, solveRate={3:.3f}, avgIter={4:.2f}, avgErr={5:.6f}, maxErr={6:.6f}, tol={7:.6f}, maxIter={8}".format(
                    base_name,
                    int(rc.get("count", 0)),
                    int(rc.get("solved", 0)),
                    float(rc.get("solveRate", 0.0)),
                    float(rc.get("avgIter", 0.0)),
                    float(rc.get("avgErr", 0.0)),
                    float(rc.get("maxErr", 0.0)),
                    float(rc.get("tol", 0.0)),
                    int(rc.get("maxIter", 0)),
                )
            )
            lines.append(
                "{0}: restoreCompAfter(mappedTarget) count={1}, max={2:.6f}, avg={3:.6f}, p95={4:.6f}, snap<=1e-6={5:.3f}".format(
                    base_name,
                    int(rc_after.get("count", 0)),
                    float(rc_after.get("max", 0.0)),
                    float(rc_after.get("avg", 0.0)),
                    float(rc_after.get("p95", 0.0)),
                    float(rc_after.get("snap", 0.0)),
                )
            )
            pvo = stat.get("postVsOriginalByMappedTarget", {})
            lines.append(
                "{0}: postVsOriginal(mappedTarget) count={1}, max={2:.6f}, avg={3:.6f}, p95={4:.6f}, snap<=1e-6={5:.3f}".format(
                    base_name,
                    int(pvo.get("count", 0)),
                    float(pvo.get("max", 0.0)),
                    float(pvo.get("avg", 0.0)),
                    float(pvo.get("p95", 0.0)),
                    float(pvo.get("snap", 0.0)),
                )
            )
        except Exception as ex:
            lines.append("{0}: ERROR {1}".format(base_name, str(ex)))
            lines.append(traceback.format_exc())
            with io.open(result_path, "w", encoding="utf-8") as wf:
                wf.write("\n".join(lines))
            raise

    with io.open(result_path, "w", encoding="utf-8") as wf:
        wf.write("\n".join(lines))


if __name__ == "__main__":
    main()
