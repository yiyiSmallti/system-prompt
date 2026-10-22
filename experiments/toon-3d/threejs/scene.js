// Cel-shaded view of a glb: MeshToonMaterial with a 3-step gradient map, inverted-hull outlines pushed
// out along the normals in object space (like the Solidify hull in Blender), and the same orthographic
// framing and key light as the Blender renders.
// OutlineEffect was tried first: it offsets in screen space along the projected normal, so on thin
// hair locks lying over the forehead the outline shell fills the gaps between locks (see README).
// ?mode=chatgpt (default) renders the procedural model exported by blender/build.py;
// ?mode=reference renders the VRoid control model (blender/render_reference.py does the same in Blender).
// render.mjs calls window.renderView(name) and reads back the canvas.
import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';

const params = new URLSearchParams(location.search);
const GLB = params.get('glb') || '../out/chatgpt_uniform.glb';
const MODE = params.get('mode') || 'chatgpt';
const SCALE = Number(params.get('scale') || 2);

const PX = 0.001775;              // metres per reference pixel, as in blender/build.py
const REF_W = 454, REF_H = 1024;
const TURN = [Math.round(REF_W * SCALE), Math.round(REF_H * SCALE)];
const turnaround = (rot, camX, soleRow) => ({ rot, center: [camX, (soleRow - REF_H / 2) * PX], height: REF_H * PX, size: TURN });
// rotation about the up axis (deg), framed centre (x, y), framed height (m), pixels
const VIEWS = {
  chatgpt: {
    front: turnaround(0, 0.0107, 989),
    left: turnaround(-90, 0.064, 980),
    right: turnaround(90, -0.046, 980),
    back: turnaround(180, -0.0036, 980),
    bust: { rot: -30, center: [0.0, 1.41], height: 0.66, size: [1024, 1024] },
  },
  reference: {
    front: { rot: 0, center: [0.0, 0.84], height: REF_H * PX, size: TURN },
    bust: { rot: -30, center: [0.0, 1.33], height: 0.55, size: [1024, 1024] },
  },
}[MODE];
// Blender sun direction (0.45, 0.75, -0.55) converted from Z-up to glTF Y-up
const LIGHT_DIR = new THREE.Vector3(0.45, -0.55, -0.75).normalize();

const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true, preserveDrawingBuffer: true });
renderer.setPixelRatio(1);
renderer.setClearColor(0x000000, 0);
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFShadowMap;
document.body.appendChild(renderer.domElement);

const scene = new THREE.Scene();
const camera = new THREE.OrthographicCamera(-1, 1, 1, -1, 0.1, 20);

// Toon lighting: one key light, with a lavender ambient that becomes the shadow tint.
const key = new THREE.DirectionalLight(0xffffff, 2.2);
key.castShadow = true;
key.shadow.mapSize.set(4096, 4096);
Object.assign(key.shadow.camera, { left: -1, right: 1, top: 1, bottom: -1, near: 0.1, far: 12 });
key.shadow.bias = -0.0004;
key.shadow.normalBias = 0.004;
key.target.position.set(0, 0.9, 0);
key.position.copy(LIGHT_DIR).multiplyScalar(-5).add(key.target.position);
scene.add(key, key.target);
scene.add(new THREE.AmbientLight(0xd4cbf2, 1.35));

// 3 tones: deep shade / shade / lit, picked by N.L remapped to [0, 1]
function gradientTexture(deep, shade) {
  const n = 16;
  const data = new Uint8Array(n * 4);
  for (let i = 0; i < n; i++) {
    const v = i < deep ? 60 : i < shade ? 150 : 255;
    data.set([v, v, v, 255], i * 4);
  }
  const tex = new THREE.DataTexture(data, n, 1, THREE.RGBAFormat);
  tex.minFilter = tex.magFilter = THREE.NearestFilter;
  tex.generateMipmaps = false;
  tex.needsUpdate = true;
  return tex;
}
const gradientMap = gradientTexture(4, 10);
// faces stay lit except where they turn fully away, matching the face thresholds in Blender
const faceGradientMap = gradientTexture(0, 1);
const isFace = (name) => name === 'face' || /^Face_|_(FACE|EYE)$/.test(name);

// object extras written by blender/build.py live on the node above the mesh primitives
function extra(obj, key) {
  for (let o = obj; o; o = o.parent) if (key in o.userData) return o.userData[key];
  return undefined;
}

// outline widths are in metres, as in blender/build.py
function outlineSettings(obj, src) {
  if (MODE === 'reference') {
    // VRoid convention: no outline on face decals and eyes; MToon's dark brown line elsewhere
    const decal = /_(FACE|EYE)$/.test(src.name);
    return { width: decal ? 0 : 0.0012, color: new THREE.Color().setRGB(0.06, 0.01, 0.01) };
  }
  const width = extra(obj, 'outline_width') ?? 0.002;
  return { width, color: new THREE.Color(src.userData.toon_line || '#000000') };
}

function toonify(root) {
  const hulls = [];
  root.traverse((obj) => {
    if (!obj.isMesh) return;
    const src = obj.material;
    const face = isFace(src.name);
    obj.castShadow = extra(obj, 'cast_shadow') !== 0;   // the hair does not, as in Blender
    obj.receiveShadow = !face;
    obj.frustumCulled = false;      // skinned meshes are posed after loading
    const base = new THREE.Color(src.userData.toon_base || '#ffffff');
    const mat = new THREE.MeshToonMaterial({
      color: src.map ? 0xffffff : base, map: src.map || null, gradientMap: face ? faceGradientMap : gradientMap,
      transparent: src.transparent, alphaTest: src.alphaTest, side: src.side,
    });
    mat.name = src.name;
    obj.material = mat;
    const { width, color } = outlineSettings(obj, src);
    if (width > 0) hulls.push([obj, width, color]);
  });
  for (const [obj, width, color] of hulls) addHull(obj, width, color);
}

// Inverted hull: a back-face-only copy pushed out along the rest-pose normals before skinning,
// so it also follows the skeleton of a skinned mesh.
function addHull(mesh, width, color) {
  const mat = new THREE.MeshBasicMaterial({ color, side: THREE.BackSide });
  mat.onBeforeCompile = (shader) => {
    shader.uniforms.outlineWidth = { value: width };
    shader.vertexShader = 'uniform float outlineWidth;\n' + shader.vertexShader.replace(
      '#include <begin_vertex>', '#include <begin_vertex>\n\ttransformed += normalize( normal ) * outlineWidth;');
  };
  const hull = mesh.clone();
  hull.material = mat;
  hull.castShadow = hull.receiveShadow = false;
  mesh.parent.add(hull);
}

// --- posing the reference model: arms from T-pose down, as in blender/render_reference.py
function rotateAbout(p, deg) {
  return new THREE.Matrix4().makeTranslation(p.x, p.y, p.z)
    .multiply(new THREE.Matrix4().makeRotationZ(THREE.MathUtils.degToRad(deg)))
    .multiply(new THREE.Matrix4().makeTranslation(-p.x, -p.y, -p.z));
}
function setWorldMatrix(obj, world) {
  obj.parent.updateMatrixWorld(true);
  obj.parent.matrixWorld.clone().invert().multiply(world).decompose(obj.position, obj.quaternion, obj.scale);
  obj.updateMatrixWorld(true);
}
function poseArmsDown(root) {
  root.updateMatrixWorld(true);
  const get = (n) => root.getObjectByName(n);
  const pos = (o) => new THREE.Vector3().setFromMatrixPosition(o.matrixWorld);
  for (const [side, deg] of [['L', -68], ['R', 68]]) {
    const names = [`J_Bip_${side}_UpperArm`, `J_Roll_${side}_UpperArm`, `J_Aim_${side}_TopsUpperArm`];
    const aim = get(`J_Aim_${side}_Shoulder`);
    const rest = Object.fromEntries(names.map((n) => [n, get(n).matrixWorld.clone()]));
    const aimRest = aim.matrixWorld.clone();
    const pivot = pos(get(names[0]));
    const elbow = pos(get(`J_Bip_${side}_LowerArm`));
    const aimHead = pos(aim);
    const turn = rotateAbout(pivot, deg);
    // the shoulder aim bone keeps pointing at the elbow
    const e0 = elbow.clone().sub(aimHead);
    const e1 = elbow.clone().applyMatrix4(turn).sub(aimHead);
    const aimDeg = THREE.MathUtils.radToDeg(Math.atan2(e0.x * e1.y - e0.y * e1.x, e0.x * e1.x + e0.y * e1.y));
    setWorldMatrix(aim, rotateAbout(aimHead, aimDeg).multiply(aimRest));
    for (const n of names) setWorldMatrix(get(n), turn.clone().multiply(rest[n]));
  }
}

let model;
const ready = new GLTFLoader().loadAsync(GLB).then((gltf) => {
  model = gltf.scene;
  toonify(model);
  if (MODE === 'reference') poseArmsDown(model);
  scene.add(model);
});

function frame({ center, height, size: [w, h] }) {
  renderer.setSize(w, h);
  const halfW = height * w / h / 2;
  Object.assign(camera, { left: -halfW, right: halfW, top: height / 2, bottom: -height / 2 });
  camera.updateProjectionMatrix();
  camera.position.set(center[0], center[1], 5);
  camera.lookAt(center[0], center[1], 0);
}

window.views = () => Object.keys(VIEWS);
window.renderView = async (name) => {
  await ready;
  const view = VIEWS[name];
  model.rotation.y = THREE.MathUtils.degToRad(view.rot);
  frame(view);
  renderer.render(scene, camera);
  return renderer.domElement.toDataURL('image/png');
};
window.glInfo = () => {
  const gl = renderer.getContext();
  const ext = gl.getExtension('WEBGL_debug_renderer_info');
  return {
    renderer: ext ? gl.getParameter(ext.UNMASKED_RENDERER_WEBGL) : gl.getParameter(gl.RENDERER),
    version: gl.getParameter(gl.VERSION),
    three: THREE.REVISION,
  };
};
window.sceneReady = ready.then(() => true, (e) => String(e));
