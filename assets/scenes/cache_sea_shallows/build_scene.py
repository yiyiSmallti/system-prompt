"""Deterministic, texture-free Cache Sea environment. Run with Blender 5.2 headless."""
from pathlib import Path
import sys, re, json, math, time, random, hashlib
from collections import Counter, deque
import bpy
import numpy as np
from mathutils import Vector, Matrix

BASE = Path(__file__).resolve().parent
ROOT = BASE.parents[2]
if str(BASE) not in sys.path:
    sys.path.insert(0, str(BASE))
TAU = math.tau
MATS, KIT, COLS = {}, {}, {}
ZONE_INFO = {
    'Channel': ('#3380FF', '#40F2D9', 5.6, 8.2),
    'Reef': ('#4CE6B4', '#7BFFD2', 5., 7.),
    'Coral': ('#FF6F9A', '#FF8AB4', 3.2, 4.6),
    'Graveyard': ('#B8404E', '#FF5A64', 4., 6.),
    'Ridge': ('#9CC2FF', '#BFD8FF', 6., 9.),
    'Canyon': ('#26C8FF', '#5CE1FF', 11., 16.),
    'Plaza': ('#DCE8FF', '#E8F0FF', 7., 10.),
}

# Authored per-cell edges keep the other three entrances clear; the kit grows west.
KELP_EDGES = {
    (17,10): 'E', (17,11): 'E', (5,12): 'W', (1,13): 'W',
    (5,13): 'N', (1,14): 'W', (11,15): 'S', (11,16): 'W',
    (4,18): 'W', (4,19): 'N', (8,20): 'S', (8,21): 'S',
}
KELP_ROTATIONS = {'W': 0., 'S': math.pi/2, 'E': math.pi, 'N': -math.pi/2}

def source_data():
    text = (ROOT/'game/Assets/SystemPrompt/Scripts/Dungeon/DungeonData.cs').read_text()
    text = text[text.index('["cache_sea_shallows"]'):text.index('["cache_sea_deep"]')]
    rows = re.findall(r'^\s*"([#.=%A-Za-z0-9]+)",\s*$', text[text.index('Map ='):text.index('Zones')], re.M)
    assert len(rows) == 23 and all(len(r) == 29 for r in rows)
    zones = []
    for m in re.finditer(r'Name = "([^"]+)", Tag = "([^"]+)", Cells = new RectInt\((\d+), (\d+), (\d+), (\d+)\), Style = ZoneStyle\.(\w+)', text):
        name, tag, x, y, w, h, style = m.groups()
        zones.append(dict(name=name, tag=tag, rect=list(map(int, (x,y,w,h))), style=style))
    return rows, zones

ROWS, ZONES = source_data()
W, H = len(ROWS[0]), len(ROWS)

def zone(c, r):
    for z in ZONES:
        x,y,w,h = z['rect']
        if x <= c < x+w and y <= r < y+h:
            return z['style']
    return 'Channel'

def zone_key(c, r):
    for z in ZONES:
        x,y,w,h = z['rect']
        if x <= c < x+w and y <= r < y+h:
            return z['tag'].replace(' ', '_')
    return 'UNZONED_CHANNEL'

def cell(c, r):
    return Vector((4*c+2, 4*(H-1-r)+2, 0))

def mark(c, r):
    return ROWS[r][c] if 0 <= c < W and 0 <= r < H else None

def hash32(c, r):
    # Keep every C# intermediate single precision, including the sine argument.
    n = np.float32(np.sin(np.float32(np.float32(c)*np.float32(127.1) + np.float32(r)*np.float32(311.7)))) * np.float32(43758.5453)
    return float(np.float32(n - np.floor(n)))

def height(c, r):
    _,_,lo,hi = ZONE_INFO[zone(c,r)]
    h = hash32(c,r)
    return hi+2+h*4 if c in (0,W-1) or r in (0,H-1) else lo+h*(hi-lo)

def islands():
    seen, result = set(), set()
    for r in range(H):
        for c in range(W):
            if mark(c,r) != '#' or (c,r) in seen:
                continue
            q, group = [(c,r)], []
            seen.add((c,r))
            while q:
                x,y = q.pop(); group.append((x,y))
                for p in ((x+1,y),(x-1,y),(x,y+1),(x,y-1)):
                    if mark(*p) == '#' and p not in seen:
                        seen.add(p); q.append(p)
            if not any(x in (0,W-1) or y in (0,H-1) for x,y in group):
                result.update(group)
    return result

ISLANDS = islands()

def rgb(hexcode):
    vals = [int(hexcode.lstrip('#')[i:i+2],16)/255 for i in (0,2,4)]
    return tuple(v/12.92 if v <= .04045 else ((v+.055)/1.055)**2.4 for v in vals)

def material(name, color, glow=0., metal=.15, glass=False, toon=True):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*rgb(color),1)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (*rgb(color),1)
    p.inputs['Metallic'].default_value = metal
    p.inputs['Roughness'].default_value = .53
    if glow:
        p.inputs['Emission Color'].default_value = (*rgb(color),1)
        p.inputs['Emission Strength'].default_value = glow
    if glass:
        # Low transmission keeps the lantern readable in EEVEE and preserves its lamp.
        p.inputs['Transmission Weight'].default_value = .78
        p.inputs['Roughness'].default_value = .14
        p.inputs['IOR'].default_value = 1.45
        m.diffuse_color = (*rgb(color),.22)
    elif not glow and toon:
        nodes=m.node_tree.nodes;links=m.node_tree.links
        diffuse=nodes.new('ShaderNodeBsdfDiffuse');diffuse.inputs['Color'].default_value=(1,1,1,1)
        light=nodes.new('ShaderNodeShaderToRGB');links.new(diffuse.outputs[0],light.inputs[0])
        ramp=nodes.new('ShaderNodeValToRGB');ramp.color_ramp.interpolation='CONSTANT'
        stops=[(.08,.40),(.24,.80),(.48,1.25),(.80,1.75)]
        for i,(position,value) in enumerate(stops):
            entry=ramp.color_ramp.elements[i] if i<2 else ramp.color_ramp.elements.new(position)
            entry.position=position;entry.color=(value,value,value,1)
        links.new(light.outputs[0],ramp.inputs[0])
        multiply=nodes.new('ShaderNodeMixRGB');multiply.blend_type='MULTIPLY';multiply.inputs[0].default_value=1
        multiply.inputs[1].default_value=(*rgb(color),1);links.new(ramp.outputs[0],multiply.inputs[2])
        output=nodes.new('ShaderNodeEmission');links.new(multiply.outputs[0],output.inputs[0])
        links.new(output.outputs[0],nodes.get('Material Output').inputs['Surface'])
    m['base_srgb'] = color
    m['emission_strength'] = glow
    MATS[name] = m
    return name

def setup_materials():
    material('M_Rack_Body','#1A2944',metal=.45)
    material('M_Rack_Trim','#0A1222',metal=.3)
    material('M_Rack_Shadow','#050A14',metal=.2)
    for z,(fc,rc,_,_) in ZONE_INFO.items():
        material('M_Rack_LED_'+z,rc,3.2)
        # The zone tint is subdued on the tile itself; pure zone colour lives in its seams.
        c = [int(fc[i:i+2],16) for i in (1,3,5)]
        dark = '#'+''.join(f'{int(a*.11+b*.89):02X}' for a,b in zip(c,(18,31,53)))
        material('M_Seabed_'+z,dark,metal=.45)
    material('M_Rock','#172942',metal=.1)
    material('M_Glass','#A3DFFF',glass=True)
    material('M_Bubble_White','#EEF3FF',.09,metal=0)
    material('M_Bubble_Green','#6EE08F',.08,metal=0)
    material('M_Bubble_Blue','#4A8BFF',.12,metal=0)
    for name,col in [('Gold','#FFD23F'),('Pink','#FF6FA8'),('Orange','#FF8A3D'),('Cyan','#5CE1FF')]:
        material('M_Coral_'+name,col,.12,metal=.05)
    material('M_Coral_Stem','#7A3A62',metal=0)
    material('M_Coral_Mound','#5A2A4A',metal=0)
    material('M_Grave','#4A5268',metal=.05)
    material('M_Phone_Body','#101A2B',metal=.6)
    material('M_Phone_Screen','#163C70',.55,metal=.15)
    material('M_Emit_ColdWhite','#DCEBFF',5.)
    material('M_Emit_Green','#4CFF99',3.2)
    material('M_Emit_Firewall','#FF455C',2.1)
    material('M_Emit_Amber','#FFB347',2.3)
    material('M_Emit_ChannelGrid','#3380FF',2.6)
    material('M_Reference','#A9B8CA',metal=.1)
    material('M_Drift_Glass','#7BCFFF',.4,glass=True)
    assert len(MATS) <= 40, len(MATS)

class Geo:
    """Small welded mesh builder. All faces are explicitly triangulated on creation."""
    def __init__(self):
        self.v=[]; self.f=[]; self.mi=[]; self.materials=[]; self.lookup={}
    def face(self, coords, mat):
        if mat not in self.materials: self.materials.append(mat)
        indices=[]
        for co in coords:
            key=tuple(round(float(x),7) for x in co)
            if key not in self.lookup:
                self.lookup[key]=len(self.v); self.v.append(tuple(co))
            indices.append(self.lookup[key])
        for i in range(1,len(indices)-1):
            self.f.append((indices[0],indices[i],indices[i+1]));self.mi.append(self.materials.index(mat))
    def box(self, at, size, mat, sides=(0,1,2,3,4,5), rot=0):
        x,y,z=at; a,b,c=[v/2 for v in size]
        cs=[(-a,-b,-c),(a,-b,-c),(a,b,-c),(-a,b,-c),(-a,-b,c),(a,-b,c),(a,b,c),(-a,b,c)]
        cr,sr=math.cos(rot),math.sin(rot)
        vs=[(x+u*cr-v*sr,y+u*sr+v*cr,z+w) for u,v,w in cs]
        fs=[(0,3,2,1),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7),(4,5,6,7)]
        for i in sides:self.face([vs[j] for j in fs[i]],mat)
    def front(self,x,z,w,h,y,mat,bevel=0):
        if not bevel:
            self.face([(x-w/2,y,z-h/2),(x+w/2,y,z-h/2),(x+w/2,y,z+h/2),(x-w/2,y,z+h/2)],mat)
            return
        outer=[(x-w/2,y+.018,z-h/2),(x+w/2,y+.018,z-h/2),(x+w/2,y+.018,z+h/2),(x-w/2,y+.018,z+h/2)]
        inner=[(x-w/2+bevel,y,z-h/2+bevel),(x+w/2-bevel,y,z-h/2+bevel),(x+w/2-bevel,y,z+h/2-bevel),(x-w/2+bevel,y,z+h/2-bevel)]
        self.face(inner,mat)
        for i in range(4):self.face([outer[i],outer[(i+1)%4],inner[(i+1)%4],inner[i]],'M_Rack_Trim')
    def tube(self, points, radius, mat, sides=5, cap=True):
        points=[Vector(p) for p in points]; rings=[]
        for i,p in enumerate(points):
            t=(points[min(i+1,len(points)-1)]-points[max(0,i-1)]).normalized()
            helper=Vector((0,0,1)) if abs(t.z)<.9 else Vector((0,1,0))
            a=t.cross(helper).normalized();b=t.cross(a).normalized()
            rings.append([p+radius*(a*math.cos(k*TAU/sides)+b*math.sin(k*TAU/sides)) for k in range(sides)])
        for a,b in zip(rings,rings[1:]):
            for i in range(sides):self.face([a[i],a[(i+1)%sides],b[(i+1)%sides],b[i]],mat)
        if cap:
            self.face(list(reversed(rings[0])),mat);self.face(rings[-1],mat)
    def cyl(self, at, r, h, mat, n=12, rtop=None, caps=True):
        rtop=r if rtop is None else rtop
        x,y,z=at
        a=[(x+r*math.cos(i*TAU/n),y+r*math.sin(i*TAU/n),z) for i in range(n)]
        b=[(x+rtop*math.cos(i*TAU/n),y+rtop*math.sin(i*TAU/n),z+h) for i in range(n)]
        for i in range(n):self.face([a[i],a[(i+1)%n],b[(i+1)%n],b[i]],mat)
        if caps:self.face(list(reversed(a)),mat);self.face(b,mat)
    def sphere(self, at, scale, mat, n=12, rings=7):
        x,y,z=at;a,b,c=scale
        v=[]
        for j in range(1,rings):
            ph=math.pi*j/rings
            v.append([(x+a*math.sin(ph)*math.cos(i*TAU/n),y+b*math.sin(ph)*math.sin(i*TAU/n),z+c*math.cos(ph)) for i in range(n)])
        for i in range(n):
            self.face([(x,y,z+c),v[0][i],v[0][(i+1)%n]],mat)
            self.face([v[-1][(i+1)%n],v[-1][i],(x,y,z-c)],mat)
        for u,w in zip(v,v[1:]):
            for i in range(n):self.face([u[i],w[i],w[(i+1)%n],u[(i+1)%n]],mat)
    def transform(self,matrix):
        self.v=[tuple(matrix@Vector(v)) for v in self.v]
        self.lookup={}
        return self
    def append(self,other,matrix=None):
        for f,mi in zip(other.f,other.mi):
            vs=[Vector(other.v[i]) for i in f]
            if matrix:vs=[matrix@v for v in vs]
            self.face(vs,other.materials[mi])
    def mesh(self,name):
        m=bpy.data.meshes.new(name)
        m.from_pydata(self.v,[],self.f)
        for mat in self.materials:m.materials.append(MATS[mat])
        for p,mi in zip(m.polygons,self.mi):
            p.material_index=mi
            name=self.materials[mi]
            p.use_smooth=name.startswith('M_Bubble_') or name in ('M_Coral_Gold','M_Coral_Pink','M_Coral_Orange','M_Coral_Cyan','M_Reference')
        m.update()
        return m

def collection(name,parent=None):
    c=bpy.data.collections.new(name)
    (parent.children if parent else bpy.context.scene.collection.children).link(c)
    return c

def obj_mesh(name,mesh,col,loc=(0,0,0),parent=None):
    o=bpy.data.objects.new(name,mesh);col.objects.link(o)
    o.parent=parent;o.location=loc
    return o

def empty(name,col,loc=(0,0,0),parent=None):
    o=obj_mesh(name,None,col,loc,parent);o.empty_display_size=.35
    return o

def tag(o,c,r,role,footprint=None):
    o['cell_c']=c;o['cell_r']=r;o['role']=role
    o['allowed_cells']=json.dumps(footprint or [[c,r]])
    return o

def text_geo(words,size,mat,at,vertical=False,back=False):
    curve=bpy.data.curves.new('TextSource','FONT');curve.body=words
    curve.font=FONT;curve.size=size;curve.align_x='CENTER';curve.align_y='CENTER'
    # Glyphs are triangulated planar inlays. Extruding every repeated Chinese contour
    # wastes the scene budget on invisible glyph sidewalls at sub-centimetre depth.
    curve.space_line=1.05;curve.resolution_u=2;curve.extrude=0
    ob=bpy.data.objects.new('TextConvert',curve);bpy.context.scene.collection.objects.link(ob)
    if vertical:ob.rotation_euler=(math.pi/2,0,math.pi if back else 0)
    ob.location=at
    bpy.context.view_layer.update()
    mesh=bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    g=Geo()
    # Font tessellation can contain concave faces; Blender's loop triangles handle them.
    mesh.calc_loop_triangles()
    for tri in mesh.loop_triangles:g.face([ob.matrix_world@mesh.vertices[i].co for i in tri.vertices],mat)
    bpy.data.objects.remove(ob,do_unlink=True);bpy.data.meshes.remove(mesh);bpy.data.curves.remove(curve)
    return g

def rack_face(variant):
    g=Geo();b='M_Rack_Body';t='M_Rack_Trim';d='M_Rack_Shadow';e='M_Rack_LED_Channel'
    # Only the exterior skin is modelled; there is no hidden cube behind each face.
    g.front(0,1.15,4,2.3,-1.82,d)
    for x in (-1.94,1.94):
        g.box((x,-1.98,1.15),(.12,.16,2.3),t,sides=(1,2,4))
        g.front(x,1.15,.027,2.18,-2.064,d)
    for z,h in ((.08,.16),(2.22,.16)):
        g.box((0,-1.96,z),(3.76,.22,h),t,sides=(1,2,4,5))
    g.front(0,2.135,3.72,.035,-2.074,e)
    for i in range(6):
        x=-1.55+i*.62;kind=(i*2+3)%5 if variant==5 else (i+variant)%5
        g.front(x,1.15,.575,1.91,-1.955,b,bevel=.022)
        # The five visible cabinet families deliberately use different silhouettes in relief.
        if kind==0:  # 1U / 2U trays
            for j in range(5):
                z=.43+j*.31
                if j==0:
                    g.box((x,-1.985,z),(.50,.065,.235),t,sides=(1,2,4,5))
                else:g.front(x,z,.50,.235,-1.985,t if j%3==0 else d)
                g.front(x-.18,z,.035,.055,-1.995,e)
            g.box((x+.22,-2.015,1.12),(.035,.06,.29),t,sides=(1,2,4,5))
        elif kind==1:  # drive cage with eight individual vertical carriers
            for j in range(4):
                xx=x-.19+j*.125
                g.front(xx,1.19,.085,1.40,-1.985,t)
                g.front(xx,1.76,.035,.055,-1.995,e)
            g.front(x,.40,.46,.07,-1.99,d)
        elif kind==2:  # patch panel and two short cable loops
            for j in range(4):
                xx=x-.18+j*.12
                g.front(xx,1.74,.065,.10,-1.985,e if j%2 else d)
            for dx in (-.13,.13):
                g.tube([(x+dx,-1.99,1.64),(x+dx,-2.07,1.39),(x+dx+.08,-2.06,1.27),(x+dx+.08,-1.99,1.64)],.017,d,3)
            g.front(x,.60,.48,.30,-1.985,t)
        elif kind==3:  # actual recessed vent louvers
            g.front(x,1.18,.50,1.52,-1.982,d)
            for j in range(6):
                g.front(x,.61+j*.23,.44,.063,-2.002,t)
            g.front(x-.17,1.98,.055,.035,-1.994,e)
        else:  # blanking plates
            for j in range(3):g.front(x,.55+j*.55,.50,.46,-1.987,b)
            g.box((x+.20,-2.015,1.12),(.035,.06,.40),t,sides=(1,2,4,5))
            g.front(x-.18,1.99,.045,.045,-1.995,e)
    return g

def floor_tile(vent=False):
    g=Geo();m='M_Seabed_Channel';e='M_Emit_ChannelGrid'
    g.face([(-2,-2,-.055),(2,-2,-.055),(2,2,-.055),(-2,2,-.055)],'M_Rack_Shadow')
    for y in range(4):
        for x in range(4):
            a,b=x-2,y-2
            if vent and (x,y)==(1,2):
                g.face([(a+.04,b+.04,-.035),(a+.96,b+.04,-.035),(a+.96,b+.96,-.035),(a+.04,b+.96,-.035)],e)
                for j in range(9):
                    v=b+.09+j*.10
                    g.face([(a+.045,v-.022,-.011),(a+.955,v-.022,-.011),(a+.955,v+.022,-.011),(a+.045,v+.022,-.011)],'M_Rack_Trim')
            else:
                outer=[(a+.018,b+.018,-.025),(a+.982,b+.018,-.025),(a+.982,b+.982,-.025),(a+.018,b+.982,-.025)]
                inner=[(a+.038,b+.038,0),(a+.962,b+.038,0),(a+.962,b+.962,0),(a+.038,b+.962,0)]
                g.face(inner,m)
                for i in range(4):g.face([outer[i],outer[(i+1)%4],inner[(i+1)%4],inner[i]],m)
    # Thin cells lines sit below the walking plane; 1m seams remain physical grooves.
    for x in (-1.992,1.992):
        g.face([(x-.006,-2,-.032),(x+.006,-2,-.032),(x+.006,2,-.032),(x-.006,2,-.032)],e)
    for y in (-1.992,1.992):
        g.face([(-1.98,y-.006,-.032),(1.98,y-.006,-.032),(1.98,y+.006,-.032),(-1.98,y+.006,-.032)],e)
    return g

def fan_geo():
    g=Geo();g.cyl((0,0,0),.12,.10,'M_Rack_Trim',8)
    for i in range(5):
        a=i*TAU/5
        pts=[(.10,0,.055),(.34,-.13,.055),(.49,.02,.055),(.29,.13,.055)]
        g.face([(x*math.cos(a)-y*math.sin(a),x*math.sin(a)+y*math.cos(a),z) for x,y,z in pts],'M_Rack_Shadow')
    return g

def low_rack():
    g=Geo();g.box((0,0,.58),(3.88,3.88,1.16),'M_Rack_Body',sides=(1,2,3,4,5))
    for k in range(4):
        face=Geo()
        for i in range(6):
            x=-1.55+i*.62
            face.front(x,.62,.53,.84,-1.955,'M_Rack_Shadow',bevel=.025)
            face.front(x,.85,.41,.035,-1.98,'M_Rack_LED_Channel')
            face.box((x+.20,-1.97,.52),(.025,.05,.20),'M_Rack_Trim',sides=(1,2,4,5))
        face.front(0,1.23,3.9,.10,-1.95,'M_Rack_Trim')
        g.append(face,Matrix.Rotation(k*math.pi/2,4,'Z'))
    g.box((0,0,1.27),(3.98,3.98,.06),'M_Rack_Trim',sides=(1,2,3,4,5))
    return g

def bubble_geo(low=False,variant=0):
    g=Geo();cols=['White','Green','Blue']
    count=1 if low else 3
    for i in range(count):
        m='M_Bubble_'+cols[(i+variant)%3]
        h=.51 if low else .87
        z=.68 if low else .96+i*1.80
        x=(.10 if i%2 else -.10);y=(i%2-.5)*.12
        # Rounded rectangular front and back, with a convex rim: a pillow, not an ellipsoid.
        profile=[];ww=3.68;hh=h*2;rr=min(.44,h*.70)
        for cx,cz,start in [(ww/2-rr,-hh/2+rr,-90),(ww/2-rr,hh/2-rr,0),(-ww/2+rr,hh/2-rr,90),(-ww/2+rr,-hh/2+rr,180)]:
            for k in range(4):
                a=math.radians(start+k*30);profile.append((cx+rr*math.cos(a),cz+rr*math.sin(a)))
        rings=[]
        for yy,scale in [(-1.88,.90),(-1.62,1.),(1.62,1.),(1.88,.90)]:
            rings.append([(x+xx*scale,y+yy,z+zz*scale) for xx,zz in profile])
        g.face(rings[0],m);g.face(list(reversed(rings[-1])),m)
        for a,b in zip(rings,rings[1:]):
            for k in range(len(a)):g.face([a[k],b[k],b[(k+1)%len(a)],a[(k+1)%len(a)]],m)
        # Solid wedge tail lies inside the cell and never drops below ground.
        tail=[(x-1.35,z-h*.58),(x-1.66,z-h*.98),(x-.90,z-h*.64)]
        for yy in (-1.72,-.94):g.face([(xx,y+yy,zz) for xx,zz in (tail if yy==-1.72 else list(reversed(tail)))],m)
        for k in range(3):
            a,b=tail[k],tail[(k+1)%3]
            g.face([(a[0],y-1.72,a[1]),(a[0],y-.94,a[1]),(b[0],y-.94,b[1]),(b[0],y-1.72,b[1])],m)
        # Small raised dot messages distinguish the rounded pillows from plain boulders.
        for dx in (-.32,0,.32):g.sphere((x+dx,y-1.90,z+.12),(.055,.05,.055),'M_Rack_Trim',6,3)
    return g

def coral_geo(variant):
    g=Geo();g.box((0,0,.08),(3.98,3.98,.16),'M_Coral_Mound',sides=(1,2,3,4,5))
    g.sphere((0,0,.05),(1.99,1.99,.66),'M_Coral_Mound',12,5)
    for i,(x,y) in enumerate([(-.92,-.65),(.88,.55),(-.38,.95),(.65,-.85)]):
        v=(variant+i)%4;z=2.65+(v*.73);r=.63 if i%2 else .70
        g.tube([(x,y,.35),(x+.12,y-.08,z*.48),(x,y,z)],.14,'M_Coral_Stem',6)
        g.sphere((x,y,z+.26),(r,r,r),'M_Coral_'+['Gold','Pink','Orange','Cyan'][v],14,8)
        # Faces on both sides; all features are geometry, never an atlas.
        for side in (-1,1):
            yy=y+side*r*.94
            for dx in (-.21,.21):
                if v==1:
                    g.tube([(x+dx-.075,yy,z+.48),(x+dx,yy-side*.035,z+.56),(x+dx+.075,yy,z+.48)],.035,'M_Rack_Shadow',4)
                else:g.sphere((x+dx,yy,z+.44),(.055,.035,.09),'M_Rack_Shadow',6,4)
            pts=[(x+u,yy,z+.17-.15*math.cos(u/.27*math.pi/2)) for u in (-.27,-.135,0,.135,.27)]
            if v==2:pts=[(px,py,z+.08) for px,py,pz in pts]
            g.tube(pts,.035,'M_Rack_Shadow',4)
    return g

def grave_geo(low=False):
    g=Geo()
    if not low:
        g.box((0,0,.14),(3.75,3.75,.28),'M_Rock')
        g.box((0,0,3.65),(2.55,1.28,6.74),'M_Grave')
        g.box((0,0,7.17),(2.95,1.65,.30),'M_Rack_Trim')
        for s in (-1,1):
            g.append(text_geo('此\n帖\n已\n删\n除',.83,'M_Emit_Firewall',(0,s*.653,3.78),True,s==1))
            for x in (-1.12,1.12):g.box((x,s*.65,3.66),(.034,.025,6.2),'M_Emit_Firewall')
    else:
        for x in (-.96,.96):
            g.box((x,0,.09),(1.74,1.40,.18),'M_Rock')
            g.box((x,0,.68),(1.52,.43,1.0),'M_Grave')
            g.sphere((x,0,1.16),(.76,.215,.14),'M_Grave',12,4)
            for s in (-1,1):g.append(text_geo('已删除',.32,'M_Emit_Firewall',(x,s*.22,.73),True,s==1))
    return g

def rounded_slab(w,d,z,h,mat,r=.28):
    g=Geo();outline=[]
    for cx,cy,start in [(w/2-r,-d/2+r,-90),(w/2-r,d/2-r,0),(-w/2+r,d/2-r,90),(-w/2+r,-d/2+r,180)]:
        for k in range(5):
            a=math.radians(start+k*90/4);outline.append((cx+r*math.cos(a),cy+r*math.sin(a)))
    low=[(x,y,z) for x,y in outline];high=[(x,y,z+h) for x,y in outline]
    g.face(list(reversed(low)),mat);g.face(high,mat)
    for i in range(len(low)):g.face([low[i],low[(i+1)%len(low)],high[(i+1)%len(low)],high[i]],mat)
    return g

def phone_geo():
    g=rounded_slab(11.35,11.1,-.24,.65,'M_Phone_Body',.58)
    g.append(rounded_slab(10.65,10.35,.413,.045,'M_Rack_Trim',.41))
    g.append(rounded_slab(10.12,9.55,.462,.025,'M_Phone_Screen',.32))
    for dx in (-.20,0,.20):g.cyl((dx,4.92,.47),.055,.02,'M_Rack_Shadow',8)
    g.cyl((1.35,4.91,.47),.13,.02,'M_Rack_Shadow',12)
    g.cyl((1.35,4.91,.493),.07,.01,'M_Rack_LED_Canyon',10)
    g.box((0,4.92,.49),(1.32,.12,.02),'M_Rack_Shadow')
    g.append(rounded_slab(5.3,2.40,.495,.055,'M_Bubble_Blue',.34),Matrix.Translation((-1.64,1.65,0)))
    g.face([(-3.9,.47,.552),(-3.5,.47,.552),(-4.1,-.12,.552)],'M_Bubble_Blue')
    g.append(text_geo('在吗',2.0,'M_Emit_ColdWhite',(-1.60,1.63,.56)))
    g.append(text_geo('未读',.36,'M_Rack_LED_Ridge',(3.6,-3.65,.50)))
    g.box((0,-4.93,.50),(1.4,.13,.025),'M_Rack_Trim')
    g.box((5.70,1,.14),(.06,.90,.19),'M_Rack_Trim')
    # Rotate within the nine-cell footprint, while keeping the high corner below 1.6m.
    g.transform(Matrix.Translation((0,0,.18)) @ Matrix.Rotation(math.radians(3.0),4,'X') @ Matrix.Rotation(math.radians(-1.8),4,'Y'))
    return g

def kelp_geo(cables=False):
    g=Geo()
    if cables:
        for i in range(7):
            x=-1.55+(i%3)*.25;y=-1.50+(i//3)*.35
            bottom=2.72+i*.18
            pts=[(x,y,11.5),(x+.16,y+.12,9),(x-.07,y+.06,6.5),(x+.15,y,bottom)]
            g.tube(pts,.055 if i%2 else .075,'M_Rack_LED_Canyon' if i%3==0 else 'M_Rack_Shadow',5)
            g.cyl((x+.15,y,bottom),.10,.15,'M_Rack_Trim',6)
    else:
        for i in range(7):
            x=-1.73+(i%2)*.28;y=-1.60+(i//2)*.80;h=1.65+(i%3)*.40
            g.tube([(x,y,.02),(x+.10,y,h*.4),(x-.03,y+.12,h*.75),(x+.12,y,h)],.035,'M_Emit_Green',4)
            for j in range(2):
                z=h*(.3+j*.25)
                g.face([(x,y,z),(x+.24,y+.14,z+.36),(x+.10,y,z+.5)],'M_Bubble_Green')
    return g

def beacon_parts():
    g=Geo();b='M_Rack_Body';t='M_Rack_Trim';e='M_Rack_LED_Reef'
    g.cyl((0,0,0),1.05,.24,b,8,rtop=.92)
    g.cyl((0,0,.24),.52,1.78,b,8,rtop=.34)
    for z,r in ((.26,.92),(.85,.46),(1.48,.40),(2.02,.57)):g.cyl((0,0,z),r,.065,e,8)
    g.cyl((0,0,2.10),.39,.48,'M_Glass',8,caps=False)
    for i in range(8):
        a=i*TAU/8;g.tube([(.40*math.cos(a),.40*math.sin(a),2.08),(.40*math.cos(a),.40*math.sin(a),2.63)],.025,t,4)
    g.cyl((0,0,2.62),.63,.40,t,8,rtop=.08)
    lamp=Geo();lamp.sphere((0,0,0),(.22,.22,.22),'M_Emit_ColdWhite',12,6)
    return [('Body',g,(0,0,0)),('Lamp',lamp,(0,0,2.35))]

def fragment_parts(index):
    base=Geo();base.cyl((0,0,0),.42,.12,'M_Rack_Body',8)
    base.cyl((0,0,.12),.31,.045,'M_Rack_LED_Channel',8)
    g=Geo();g.box((0,0,0),(.91,.065,1.07),'M_Phone_Screen')
    g.front(0,0,.85,1.0,-.04,'M_Bubble_Blue',bevel=.03)
    for i in range(4):g.front(-.08,.18-i*.15,.48-(i%2)*.13,.025,-.066,'M_Emit_ColdWhite')
    if index==0:g.front(0,.41,.65,.09,-.064,'M_Emit_Firewall')
    elif index==1:g.front(.26,-.35,.13,.11,-.064,'M_Bubble_Green')
    else:g.tube([(.24,-.065,-.35),(.35,-.065,-.19)],.037,'M_Emit_Amber',4)
    return [('Base',base,(0,0,0)),('Card',g,(0,0,1.2))]

def chest_parts():
    g=Geo();g.box((0,0,.28),(1,.70,.56),'M_Rack_Body')
    for x in (-.42,.42):g.box((x,0,.28),(.07,.72,.56),'M_Rack_Trim')
    g.front(0,.32,.25,.15,-.36,'M_Emit_Amber',bevel=.02)
    lid=rounded_slab(1,.70,0,.14,'M_Rack_Trim',.08)
    lid.transform(Matrix.Translation((0,-.35,0)))
    return [('Body',g,(0,0,0)),('Lid',lid,(0,.35,.56))]

def terminal_geo(reset=False):
    g=Geo();e='M_Emit_Amber' if reset else 'M_Emit_Green';h=1.03 if reset else 1.40
    g.box((0,0,.08),(1.1,.82,.16),'M_Rack_Trim')
    g.box((0,.10,h*.42),(.73,.53,h*.74),'M_Rack_Body')
    g.box((0,0,h-.22),(1.0,.70,.38),'M_Rack_Trim')
    g.front(0,h-.19,.83,.28,-.36,'M_Rack_Shadow',bevel=.025)
    g.front(0,h-.17,.71,.19,-.367,e)
    for i in range(4):g.front(-.23+i*.16,h-.42,.09,.045,-.366,e if i==0 else 'M_Rack_Shadow')
    g.append(text_geo('RESET' if reset else 'LINK',.11,'M_Emit_ColdWhite',(0,-.378,h-.17),True))
    return g

def portal_parts(gate=False):
    g=Geo();e='M_Emit_Amber' if gate else 'M_Emit_Firewall'
    for x in (-1.80,1.80):
        g.box((x,0,1.95),(.40,.65,3.90),'M_Rack_Body')
        g.front(x,1.95,.14,3.58,-.335,e)
        g.box((x,0,.12),(.40,.90,.24),'M_Rack_Trim')
    g.box((0,0,3.88),(3.20,.65,.24),'M_Rack_Trim')
    g.front(0,3.88,1.35,.10,-.34,e)
    p=Geo()
    if gate:
        for x in (-.8,.8):
            p.box((x,0,1.80),(1.55,.16,3.58),'M_Rack_Body')
            p.front(x,1.8,.035,3.44,-.09,e)
            for j in range(4):p.front(x,.6+j*.7,1.3,.06,-.09,'M_Rack_Trim')
    else:
        # Discrete luminous strips retain a view of the lighthouse through the closed barrier.
        for i in range(12):p.box((-1.5+i*.273,0,1.82),(.043,.055,3.50),e)
        for i in range(8):p.box((0,0,.3+i*.425),(3.12,.06,.035),e)
    return [('Frame',g,(0,0,0)),('Door' if gate else 'Barrier',p,(0,0,0))]

def cache_geo():
    g=Geo();s=2.5
    g.box((0,0,1.25),(2.22,2.22,2.22),'M_Rack_Body')
    for axis in range(3):
        for a in (-1,1):
            for b in (-1,1):
                at=[a*1.15,b*1.15,1.25];dims=[.20,.20,.20]
                if axis==0:at=[0,a*1.15,1.25+b*1.15]
                elif axis==1:at=[a*1.15,0,1.25+b*1.15]
                dims[axis]=2.5
                g.box(at,dims,'M_Rack_Trim')
    for k in range(4):
        f=Geo();f.front(0,1.3,1.60,1.50,-1.12,'M_Phone_Screen',bevel=.04)
        for i in range(3):f.front(0,.92+i*.3,1.16,.055,-1.145,'M_Rack_LED_Canyon')
        # A simple geometric cache icon avoids needless glyph triangulation on four faces.
        f.front(0,1.91,.54,.10,-1.15,'M_Emit_ColdWhite')
        g.append(f,Matrix.Rotation(k*math.pi/2,4,'Z'))
    return g

def socket_geo():
    g=Geo()
    for x in (-1.4,1.4):g.box((x,0,.045),(.20,3,.09),'M_Rack_Trim')
    for y in (-1.4,1.4):g.box((0,y,.045),(2.6,.20,.09),'M_Rack_Trim')
    for x in (-1.27,1.27):g.box((x,0,.026),(.025,2.54,.025),'M_Emit_Amber')
    for y in (-1.27,1.27):g.box((0,y,.026),(2.54,.025,.025),'M_Emit_Amber')
    return g

def foot_clutter_geo():
    g=Geo()
    # Kept within the wall's 15cm projection allowance; never in the aisle centre.
    g.tube([(-1.5,-2.015,.085),(-.95,-2.04,.10),(-.50,-2.03,.065),(.15,-2.02,.09)],.035,'M_Rack_Shadow',4)
    g.box((.55,-2.025,.065),(.31,.10,.12),'M_Rack_Trim',rot=.09)
    g.box((.89,-2.02,.08),(.12,.085,.15),'M_Rack_LED_Canyon',rot=.35)
    return g

def lighthouse_parts():
    g=Geo();random.seed(912)
    # Exact 12x8 footprint below 3m. Faceted shore, rings rise to an asymmetric island crest.
    rings=[];n=20
    for z,sx,sy in [(0,5.96,3.96),(.9,5.40,3.60),(2.8,4.95,3.35),(4.1,3.8,2.8)]:
        ring=[]
        for i in range(n):
            a=i*TAU/n
            rr=.90+random.random()*.10
            ring.append((math.cos(a)*sx*rr,math.sin(a)*sy*rr,z+(random.random()*.25 if z else 0)))
        rings.append(ring)
    for a,b in zip(rings,rings[1:]):
        for i in range(n):g.face([a[i],a[(i+1)%n],b[(i+1)%n],b[i]],'M_Rock' if i%3 else 'M_Rack_Body')
    g.face(rings[-1],'M_Rock')
    # Triangular rock chips stay on the island rather than invading the plaza.
    for i in range(20):
        a=i*TAU/20;rr=2.4+(i%4)*.35
        g.sphere((math.cos(a)*rr*1.18,math.sin(a)*rr*.75,.5+(i%3)*.3),(.52,.50,.65),'M_Rock',5,3)
    for i in range(18):
        a=i*TAU/18
        g.sphere((4.4*math.cos(a),2.5*math.sin(a),1.25),(1.04,.80,1.25+(i%3)*.12),'M_Rock',5,4)
    levels=[(10.0,7.6,4.15,3.0),(8.8,6.7,4.4,7.15),(7.6,5.7,4.3,11.55),(6.4,4.8,4.1,15.85),(5.1,4.0,3.9,19.95),(4.2,3.4,3.65,23.85),(3.3,3.0,3.25,27.5)]
    for i,(w,d,h,z) in enumerate(levels):
        a=math.radians(8*i)
        m=Matrix.Translation((0,0,z))@Matrix.Rotation(a,4,'Z')
        tier=Geo();tier.box((0,0,h/2),(w,d,h),'M_Rack_Shadow',sides=(1,2,3,4,5))
        for k in range(4):
            length=w if k%2==0 else d
            depth=d if k%2==0 else w
            f=Geo()
            count=max(3,int(length/.67));cw=(length-.28)/count
            for col in range(count):
                x=-length/2+.14+cw*(col+.5)
                for row in range(2):
                    zh=(h-.25)/2;cz=.16+zh*(row+.5)
                    f.front(x,cz,cw-.045,zh-.09,-depth/2-.018,'M_Rack_Body',bevel=.025)
                    for j in range(4):
                        f.front(x,cz-zh*.3+j*zh*.19,cw*.73,.07,-depth/2-.041,'M_Rack_Trim')
                        if (j+col+i)%3==0:f.front(x-.12,cz-zh*.3+j*zh*.19,.11,.045,-depth/2-.052,'M_Rack_LED_Plaza')
            for x in (-length/2+.06,length/2-.06):f.box((x,-depth/2-.025,h/2),(.12,.16,h),'M_Rack_Trim',sides=(1,2,4,5))
            f.front(0,h-.10,length,.08,-depth/2-.06,'M_Rack_LED_Ridge')
            tier.append(f,Matrix.Rotation(k*math.pi/2,4,'Z'))
        tier.box((0,0,h+.06),(w+.44,d+.44,.12),'M_Rack_Trim',sides=(1,2,3,4,5))
        # Alternating exterior balconies are usable 1m railings, never solid boxes.
        if i in (0,2,4,6):
            for side in (-1,1):
                yy=side*(d/2+.47)
                tier.box((0,yy,h+.03),(w+.7,.82,.12),'M_Rack_Body',sides=(1,2,3,4,5))
                for x in np.linspace(-w/2-.3,w/2+.3,max(4,int(w))):
                    tier.tube([(float(x),yy+side*.36,h+.10),(float(x),yy+side*.36,h+1.05)],.031,'M_Rack_Trim',5)
                for rz in (.46,1.05):tier.tube([(-w/2-.3,yy+side*.36,h+rz),(w/2+.3,yy+side*.36,h+rz)],.035,'M_Rack_Trim',5)
        g.append(tier,m)
    for i in range(12):
        a=i*TAU/12;k=i%4;start=11+k*4
        x,y=(4.4-.55*k)*math.cos(a),(3.35-.30*k)*math.sin(a)
        g.tube([(x,y,start),(x*1.10,y*1.10,start-2),(5.4*math.cos(a),4.0*math.sin(a),6),(5.4*math.cos(a),3.45*math.sin(a),2.0)],.07+(i%3)*.025,'M_Rack_Shadow',5)
    # Open glass lantern with cold-white core and a complete octagonal exterior gallery.
    z=31.05
    g.cyl((0,0,z),2.15,.20,'M_Rack_Trim',16)
    for i in range(16):
        a=i*TAU/16;x,y=2.02*math.cos(a),2.02*math.sin(a)
        g.tube([(x,y,z+.20),(x,y,z+1.22)],.036,'M_Rack_Trim',5)
    for zz in (z+.65,z+1.22):
        g.tube([(2.02*math.cos(i*TAU/16),2.02*math.sin(i*TAU/16),zz) for i in range(17)],.04,'M_Rack_Trim',5)
    g.cyl((0,0,31.25),1.43,3.30,'M_Glass',12,caps=False)
    for i in range(12):
        a=i*TAU/12;x,y=1.46*math.cos(a),1.46*math.sin(a)
        g.tube([(x,y,31.25),(x,y,34.55)],.055,'M_Rack_Trim',5)
    g.cyl((0,0,34.55),1.80,.20,'M_Rack_Trim',12)
    g.cyl((0,0,34.75),1.86,1.05,'M_Rack_Body',12,rtop=.25)
    g.cyl((0,0,35.8),.14,.78,'M_Rack_Trim',8,rtop=.045)
    lamp=Geo();lamp.cyl((0,0,-.80),.60,1.60,'M_Emit_ColdWhite',16)
    for zz in (-.86,.79):lamp.cyl((0,0,zz),.82,.07,'M_Rack_Trim',16)
    return [('Structure',g,(0,0,0)),('Lamp',lamp,(0,0,33.2))]

def register(name,parts,description):
    # All kit roots use the centre of their nominal footprint at ground level.
    index=len(KIT);display=(150+(index%8)*17,-25-(index//8)*18,0)
    root=empty('KIT_'+name,COLS['Kit'],display)
    root['kit_name']=name;root['description']=description
    for suffix,geo,at in parts:
        o=obj_mesh('KIT_'+name+'_'+suffix,geo.mesh(name+'_'+suffix),COLS['Kit'],at,root)
        o['kit_part']=suffix
    KIT[name]=root
    return root

def register_geo(name,g,description):
    return register(name,[('Mesh',g,(0,0,0))],description)

def spawn(name,c,r,col,role,rotation=0,at=None,allowed=None):
    source=KIT[name];root=empty(f'{name}_c{c:02d}_r{r:02d}',col,cell(c,r) if at is None else at,MAIN)
    root.rotation_euler.z=rotation;tag(root,c,r,role,allowed)
    for child in source.children:
        suffix=child['kit_part']
        object_name='Lighthouse_Lamp' if name=='Lighthouse' and suffix=='Lamp' else root.name+'_'+suffix
        o=obj_mesh(object_name,child.data,col,child.location,root)
        tag(o,c,r,role,allowed);o['kit_name']=name
    return root

def make_kit():
    for i in range(6):register_geo(f'RackFacade_V{i+1:02d}',rack_face(i),'4m / 2.30m layer; front -Y; open back')
    register_geo('RackTop',Geo(),'replaced below')
    # A top cap has no hidden underside, and no four vertical internal faces.
    g=Geo();g.face([(-2,-2,0),(2,-2,0),(2,2,0),(-2,2,0)],'M_Rack_Body')
    for x in (-1.72,1.72):g.box((x,0,.022),(.035,3.55,.044),'M_Rack_Trim',sides=(1,2,3,4,5))
    child=KIT['RackTop'].children[0];old=child.data;child.data=g.mesh('RackTop');bpy.data.meshes.remove(old)
    register('RackFan',[('Fan',fan_geo(),(0,0,0))],'0.98m rotor; origin at rotation centre')
    for vent in (False,True):register_geo('Seabed_Vent' if vent else 'Seabed_Tile',floor_tile(vent),'4x4m; sixteen bevelled 1m tiles')
    register_geo('LowRack',low_rack(),'4x4m low wall, 1.30m maximum')
    for i in range(3):register_geo(f'BubbleStack_{i+1}',bubble_geo(False,i),'4x4m three chat pillows')
    register_geo('BubbleLow',bubble_geo(True),'single 1.19m chat pillow')
    for i in range(3):register_geo(f'EmojiCoral_{i+1}',coral_geo(i),'cell-contained mound and four emoji stalks')
    register_geo('GraveTall',grave_geo(),'two-sided Chinese mesh inscription; 7.32m')
    register_geo('GraveLowPair',grave_geo(True),'two headstones; 1.30m maximum')
    register_geo('SunkenPhone',phone_geo(),'12x12m footprint; inclined screen with unanswered 在吗')
    register_geo('KelpEdge',kelp_geo(),'west-edge mother mesh; rotate per KELP_EDGES, keep three entrances clear')
    register_geo('CableHanging',kelp_geo(True),'7 strands; lower end above 2.7m')
    register('Beacon',beacon_parts(),'octagonal 3.02m beacon; independent lamp')
    for i,name in enumerate(('Post','Message','Draft')):register('Fragment_'+name,fragment_parts(i),'1.2m floating story card, independent Card')
    register('DataChest',chest_parts(),'1.0x0.7x0.7m chest; Lid origin on hinge')
    register_geo('Terminal',terminal_geo(),'1.40m green control console')
    register_geo('ResetConsole',terminal_geo(True),'1.03m amber console')
    register('Firewall',portal_parts(),'4x4m frame; independently hidden Barrier')
    register('Gate',portal_parts(True),'4x4m frame; independent Door')
    register_geo('CacheBlock',cache_geo(),'exact 2.50m frame cube')
    register_geo('Socket',socket_geo(),'3x3m recessed socket frame')
    register('Lighthouse',lighthouse_parts(),'12x8m footprint; Lamp origin at z33.2m')
    register_geo('WallFoot_Cables',foot_clutter_geo(),'P2 foot cables/hardware/data chip, maximum projection <0.1m')

def recolor_mesh(mesh,style,kind='rack'):
    key=(mesh.name,style,kind)
    if key in RECOLOR:return RECOLOR[key]
    out=mesh.copy();out.name=mesh.name+'_'+style
    for i,m in enumerate(out.materials):
        if m.name.startswith('M_Rack_LED_'):out.materials[i]=MATS['M_Rack_LED_'+style]
        if m.name=='M_Emit_ChannelGrid' and style!='Channel':out.materials[i]=MATS['M_Rack_LED_'+style]
        if m.name.startswith('M_Seabed_'):out.materials[i]=MATS['M_Seabed_'+style]
    RECOLOR[key]=out
    return out

RECOLOR={}
CLIPPED={}

def truncated_facade(mesh,limit):
    key=(mesh.name,round(limit,7))
    if key in CLIPPED:return CLIPPED[key]
    g=Geo();body_limit=max(0.,limit-min(.16,limit*.6))
    for poly in mesh.polygons:
        vs=[mesh.vertices[i].co.copy() for i in poly.vertices];out=[];prev=vs[-1]
        for p in vs:
            a,b=prev.z<=body_limit,p.z<=body_limit
            if a!=b:
                t=(body_limit-prev.z)/(p.z-prev.z);out.append(prev+(p-prev)*t)
            if b:out.append(p)
            prev=p
        clean=[]
        for p in out:
            if not clean or (p-clean[-1]).length>1e-7:clean.append(p)
        if len(clean)>2 and (clean[0]-clean[-1]).length<1e-7:clean.pop()
        if len(clean)<3:continue
        for i in range(1,len(clean)-1):
            tri=[clean[0],clean[i],clean[i+1]]
            if (tri[1]-tri[0]).cross(tri[2]-tri[0]).length>1e-9:g.face(tri,mesh.materials[poly.material_index].name)
    cap_height=min(.16,limit*.6)
    g.box((0,-1.96,limit-cap_height/2),(3.76,.22,cap_height),'M_Rack_Trim',sides=(1,2,4,5))
    led=next(m.name for m in mesh.materials if m.name.startswith('M_Rack_LED_'))
    led_h=min(.035,limit*.1)
    led_z=max(led_h/2,min(limit-led_h/2,body_limit-led_h/2))
    g.front(0,led_z,3.72,led_h,-2.074,led)
    result=g.mesh(mesh.name+'_Cut_'+str(round(limit,5)));CLIPPED[key]=result
    return result

def build_layout():
    meta=[];faces=0
    # S, E, N, W: the reusable face always starts at the south edge (-Y).
    dirs=[('S',0,1,0),('E',1,0,math.pi/2),('N',0,-1,math.pi),('W',-1,0,math.pi*1.5)]
    for r in range(H):
        for c in range(W):
            ch=mark(c,r);style=zone(c,r);col=ZONE_COLS[zone_key(c,r)]
            if ch!='#':
                f=spawn('Seabed_Vent' if hash32(c+70,r+80)<.12 else 'Seabed_Tile',c,r,col,'floor')
                for o in f.children:o.data=recolor_mesh(o.data,style,'floor')
            role=None;expected=0
            if ch=='#':
                if style=='Reef':
                    spawn('BubbleStack_'+str(1+(c+r)%3),c,r,col,'wall_special');role='bubble';expected=5.7
                elif style=='Coral' and (c,r) in ISLANDS:
                    spawn('EmojiCoral_'+str(1+(c+r)%3),c,r,col,'wall_special');role='coral';expected=5.6
                elif style=='Graveyard' and (c,r) in ISLANDS:
                    spawn('GraveTall',c,r,col,'wall_special');role='grave';expected=7.32
                else:
                    ht=height(c,r);expected=ht;role='rack'
                    group=tag(empty(f'Rack_c{c:02d}_r{r:02d}',col,cell(c,r),MAIN),c,r,'wall')
                    group['target_height']=ht
                    n=max(1,math.ceil(ht/2.3))
                    for label,dx,dy,angle in dirs:
                        if mark(c+dx,r+dy)=='#':continue
                        faces+=1
                        for layer in range(n):
                            remain=min(2.3,ht-layer*2.3)
                            idx=int(hash32(c*5+layer,r*7+ord(label)) * 5.99)+1
                            src=KIT[f'RackFacade_V{idx:02d}'].children[0]
                            data=recolor_mesh(src.data,style)
                            if remain<2.29999:data=truncated_facade(data,remain)
                            o=obj_mesh(f'Rack_c{c:02d}_r{r:02d}_{label}_L{layer:02d}',data,col,(0,0,layer*2.3),group)
                            o.rotation_euler.z=angle
                            # A subtle inward step each storey gives the canyon a cabinet-stack profile.
                            inset=.018 if layer%2 else 0
                            o.location.x-=dx*inset;o.location.y+=dy*inset
                            tag(o,c,r,'wall')
                        if hash32(c*11+ord(label),r*19)<.23:
                            src=KIT['WallFoot_Cables'].children[0]
                            o=obj_mesh(f'Rack_c{c:02d}_r{r:02d}_{label}_FootClutter',recolor_mesh(src.data,style),col,(0,0,0),group)
                            o.rotation_euler.z=angle;tag(o,c,r,'wall');o['p2_clutter']=True
                    src=KIT['RackTop'].children[0]
                    top=obj_mesh(f'Rack_c{c:02d}_r{r:02d}_Top',src.data,col,(0,0,ht-.10),group);tag(top,c,r,'wall')
                    fan=obj_mesh(f'Rack_c{c:02d}_r{r:02d}_Fan',KIT['RackFan'].children[0].data,col,(.65,-.55,ht-.10),group);tag(fan,c,r,'wall')
            elif ch=='=':
                if style=='Channel':role='phone'
                else:
                    name='GraveLowPair' if style=='Graveyard' else 'BubbleLow' if style in ('Reef','Coral') else 'LowRack'
                    root=spawn(name,c,r,col,'low_wall');role=name
                    if name=='LowRack':
                        for o in root.children:o.data=recolor_mesh(o.data,style)
                    expected=1.3
            elif ch=='%':
                if style=='Canyon':
                    spawn('CableHanging',c,r,COLS['Props'],'plant')
                else:
                    edge=KELP_EDGES[(c,r)]
                    kelp=spawn('KelpEdge',c,r,COLS['Props'],'plant',rotation=KELP_ROTATIONS[edge])
                    for o in [kelp,*kelp.children]:o['kelp_edge']=edge
            meta.append(dict(c=c,r=r,mark=ch,style=style,role=role,target_height=expected))
    spawn('Lighthouse',14,3,COLS['Landmarks'],'lighthouse',at=(58,76,0),allowed=[[x,y] for y in (3,4) for x in (13,14,15)])
    spawn('SunkenPhone',14,13,COLS['Landmarks'],'phone',allowed=[[x,y] for y in (12,13,14) for x in (13,14,15)])
    fragment=0
    for r,row in enumerate(ROWS):
        for c,ch in enumerate(row):
            name={'B':'Beacon','C':'DataChest','T':'Terminal','K':'CacheBlock','O':'Socket','R':'ResetConsole','X':'Firewall','Y':'Gate'}.get(ch)
            if ch=='F':name='Fragment_'+('Post','Message','Draft')[fragment];fragment+=1
            if name:
                angle=0
                if ch in 'XY':
                    opaque=lambda x,y:mark(x,y) in ('#','L','X','Y')
                    along_x=not(opaque(c,r-1) and opaque(c,r+1))
                    angle=0 if along_x else math.pi/2
                spawn(name,c,r,COLS['Props'],'prop',rotation=angle)
    return meta,faces

def build_outer():
    # Optional P2 follows the same six-cell-ring selection and height formula as BuildWalls.
    cubes=Geo()
    selected=[]
    for r in range(-6,H+6):
        for c in range(-6,W+6):
            if 0<=c<W and 0<=r<H:continue
            if math.hypot(c-14,r-3.5)<3.2:continue
            ring=max(-c,c-W+1,-r,r-H+1)
            if hash32(c,r)<.35+ring*.06:continue
            ht=9+hash32(r,c)*(6+ring*2.5)
            selected.append((c,r,ht))
    occupied={(c,r) for c,r,ht in selected}
    for c,r,ht in selected:
        p=cell(c,r);g=Geo()
        # Distant stacks use a deliberately simpler external silhouette, with real tier ledges.
        for dx,dy,angle in ((0,1,0),(1,0,math.pi/2),(0,-1,math.pi),(-1,0,math.pi*1.5)):
            if (c+dx,r+dy) in occupied:continue
            f=Geo();f.front(0,ht/2,4,ht,-1.98,'M_Rack_Body')
            for j in range(int(ht/2.3)):
                f.front(0,j*2.3+.2,3.72,.045,-2.006,'M_Rack_LED_Canyon')
                for x in (-1.25,0,1.25):f.front(x,j*2.3+1.1,.055,1.7,-2.008,'M_Rack_Shadow')
            g.append(f,Matrix.Rotation(angle,4,'Z'))
        g.face([(-2,-2,ht),(2,-2,ht),(2,2,ht),(-2,2,ht)],'M_Rack_Body')
        obj_mesh(f'Outer_c{c}_r{r}',g.mesh(f'Outer_{c}_{r}'),COLS['Outer'],p,OUTER_ROOT)
    return len(selected)

def build_drift():
    random.seed(51)
    for i in range(48):
        c=random.randint(1,27);r=random.randint(1,21)
        z=6.5+random.random()*19
        p=cell(c,r);p.z=z
        g=Geo()
        if i%4==0:
            g.sphere((0,0,0),(.46,.18,.33),'M_Drift_Glass',10,5)
            g.face([(-.24,-.02,-.2),(-.37,0,-.43),(-.05,0,-.2)],'M_Drift_Glass')
        elif i%4==1:
            g.sphere((0,0,0),(.22,.22,.22),'M_Bubble_Blue',8,5)
            for x in (-.065,.065):g.sphere((x,-.21,.035),(.025,.020,.03),'M_Rack_Shadow',6,3)
            g.tube([(-.07,-.21,-.07),(0,-.23,-.10),(.07,-.21,-.07)],.016,'M_Rack_Shadow',4)
        elif i%4==2:
            g.box((0,0,0),(.53,.025,.70),'M_Drift_Glass')
            for j in range(3):g.front(0,.17-j*.15,.36,.025,-.02,'M_Rack_LED_Canyon')
        else:g.box((0,0,0),(.16,.16,.16),'M_Rack_LED_Canyon',rot=.4)
        o=obj_mesh(f'Drift_{i:02d}',g.mesh(f'Drift_{i:02d}'),COLS['Drift'],p,DRIFT_ROOT)
        o.rotation_euler.z=random.random()*TAU

def mannequin_geo(h):
    g=Geo();m='M_Reference';s=h/1.75
    g.sphere((0,0,1.60*s),(.135*s,.125*s,.15*s),m,12,7)
    g.sphere((0,0,1.12*s),(.22*s,.135*s,.36*s),m,12,7)
    for side in (-1,1):
        g.tube([(side*.11*s,0,.83*s),(side*.13*s,0,.44*s),(side*.13*s,-.01,.08*s)],.08*s,m,8)
        g.tube([(side*.21*s,0,1.35*s),(side*.29*s,0,1.05*s),(side*.30*s,-.02,.81*s)],.062*s,m,7)
        g.sphere((side*.13*s,-.05,.065*s),(.09*s,.17*s,.065*s),'M_Rack_Trim',8,4)
    return g

def setup_reference():
    for h,position in ((1.75,(57.3,20,0)),(1.62,(58.55,21.2,0))):
        o=obj_mesh(f'Reference_{h:.2f}m',mannequin_geo(h).mesh(f'Mannequin{h}'),COLS['Reference'],position)
        o['reference_height']=h
    # Lighting is preview-only; no lights, cameras or people are exported.
    world=bpy.data.worlds.new('CacheSea_DeepBlue');bpy.context.scene.world=world
    world.use_nodes=True;world.node_tree.nodes['Background'].inputs[0].default_value=(.015,.032,.075,1)
    world.node_tree.nodes['Background'].inputs[1].default_value=.45
    # Preview-only procedural world ceiling: no bitmap, UV, or exported water mesh.
    nodes=world.node_tree.nodes;links=world.node_tree.links
    tc=nodes.new('ShaderNodeTexCoord');normal=tc.outputs['Normal']
    noise=nodes.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=6;noise.inputs['Detail'].default_value=2
    links.new(normal,noise.inputs['Vector'])
    warp=nodes.new('ShaderNodeVectorMath');warp.operation='SCALE';warp.inputs['Scale'].default_value=.38;links.new(noise.outputs['Color'],warp.inputs[0])
    add=nodes.new('ShaderNodeVectorMath');add.operation='ADD';links.new(normal,add.inputs[0]);links.new(warp.outputs[0],add.inputs[1])
    cells=nodes.new('ShaderNodeTexVoronoi');cells.feature='DISTANCE_TO_EDGE';cells.inputs['Scale'].default_value=21;links.new(add.outputs[0],cells.inputs['Vector'])
    ramp=nodes.new('ShaderNodeValToRGB')
    for i,(p,c) in enumerate([(0.,(.012,.12,.32,1)),(.018,(.004,.040,.13,1)),(.075,(.003,.015,.050,1))]):
        entry=ramp.color_ramp.elements[i] if i<2 else ramp.color_ramp.elements.new(p)
        entry.position=p;entry.color=c
    links.new(cells.outputs['Distance'],ramp.inputs[0])
    sep=nodes.new('ShaderNodeSeparateXYZ');links.new(normal,sep.inputs[0])
    ab=nodes.new('ShaderNodeMath');ab.operation='ABSOLUTE';links.new(sep.outputs['Z'],ab.inputs[0])
    mask=nodes.new('ShaderNodeMapRange');mask.inputs['From Min'].default_value=.18;mask.inputs['From Max'].default_value=.55;links.new(ab.outputs[0],mask.inputs[0])
    mix=nodes.new('ShaderNodeMixRGB');mix.inputs[1].default_value=(.006,.017,.038,1);links.new(mask.outputs[0],mix.inputs[0]);links.new(ramp.outputs[0],mix.inputs[2])
    links.new(mix.outputs[0],nodes.get('Background').inputs[0])
    data=bpy.data.lights.new('Sea_Key','SUN');data.energy=2.0;data.angle=.12;data.color=(.55,.74,1.)
    o=obj_mesh('Sea_Key',data,COLS['Reference']);o.rotation_euler=(.35,-.50,-.4)
    data=bpy.data.lights.new('Sea_Fill','SUN');data.energy=.65;data.color=(.30,.60,1.)
    o=obj_mesh('Sea_Fill',data,COLS['Reference']);o.rotation_euler=(.9,.6,2.5)
    camera=bpy.data.cameras.new('PreviewCamera');o=obj_mesh('PreviewCamera',camera,COLS['Reference']);bpy.context.scene.camera=o
    o.location=(58,8.7,2.4);camera.lens=21
    o.rotation_euler=(Vector((58,55,10))-o.location).to_track_quat('-Z','Y').to_euler()
    scene=bpy.context.scene
    try:scene.render.engine='BLENDER_EEVEE_NEXT'
    except TypeError:scene.render.engine='BLENDER_EEVEE'
    scene.render.resolution_x=1600;scene.render.resolution_y=900;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.view_settings.view_transform='AgX'
    scene.view_settings.look='AgX - Medium High Contrast'
    scene.render.film_transparent=False
    scene.world.color=(.01,.02,.05)
    scene['coordinate_mapping']='Scene FBX: temporary world Rz(180), forward -Z / up Y => Unity (X,Z,Y)'
    scene['kit_coordinate_mapping']='Kit FBX: no pre-rotation, forward -Z / up Y => Unity (-X,Z,-Y); front +Z'
    COLS['Kit'].hide_render=True
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                region=area.spaces.active.region_3d
                region.view_location=(58,46,0);region.view_distance=155
                region.view_rotation=(1,0,0,0);region.view_perspective='ORTHO'
                area.spaces.active.shading.color_type='MATERIAL'

def stats_and_save(start,meta,exposed,outer_count):
    bpy.context.view_layer.update()
    scene=bpy.context.scene
    scene['map_rows']=json.dumps(ROWS);scene['zones']=json.dumps(ZONES,ensure_ascii=False)
    scene['build_seconds']=round(time.perf_counter()-start,3)
    scene['kit_names']=json.dumps(list(KIT))
    def tris(o):return len(o.data.polygons) if o.type=='MESH' else 0
    names=('Layout','Landmarks','Props')
    counts={n:sum(tris(o) for o in COLS[n].all_objects) for n in names+('Outer','Drift')}
    kits={}
    for n,root in KIT.items():
        vs=[Vector(child.location)+v.co for child in root.children if child.type=='MESH' for v in child.data.vertices]
        mn=[min(v[i] for v in vs) for i in range(3)];mx=[max(v[i] for v in vs) for i in range(3)]
        kits[n]=dict(triangles=sum(tris(o) for o in root.children),min=mn,max=mx,size=[mx[i]-mn[i] for i in range(3)],description=root['description'])
    total=sum(counts[n] for n in names)
    report=dict(map_size=[W,H],world_size=[W*4,H*4],counts=dict(Counter(''.join(ROWS))),triangles=counts,total_triangles=total,kit=kits,materials=[dict(name=m.name,color=m['base_srgb'],emission=m['emission_strength']) for m in MATS.values()],cells=meta,exposed_rack_faces=exposed,outer_cells=outer_count,build_seconds=scene['build_seconds'])
    (BASE/'build_report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
    assert total<=600000, f'Main triangle budget: {total}'
    assert kits['Lighthouse']['triangles']<=60000
    assert all(kits[f'RackFacade_V{i:02d}']['triangles']<=1500 for i in range(1,7))
    assert kits['Seabed_Vent']['triangles']<=300
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(BASE/'cache_sea_shallows.blend'),check_existing=False)
    print('BUILD_REPORT',json.dumps({k:report[k] for k in ('total_triangles','triangles','build_seconds','exposed_rack_faces','outer_cells')}),flush=True)

def main():
    global FONT,MAIN,OUTER_ROOT,DRIFT_ROOT,ZONE_COLS
    start=time.perf_counter()
    bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
    for c in list(bpy.data.collections):bpy.data.collections.remove(c)
    for m in list(bpy.data.materials):bpy.data.materials.remove(m)
    scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene.unit_settings.scale_length=1.;scene.unit_settings.length_unit='METERS'
    for n in ('Kit','Layout','Landmarks','Props','Outer','Drift','Reference'):COLS[n]=collection(n)
    ZONE_COLS={}
    for z in ZONES:ZONE_COLS[z['tag'].replace(' ','_')]=collection(z['tag'].replace(' ','_'),COLS['Layout'])
    ZONE_COLS['UNZONED_CHANNEL']=collection('UNZONED_CHANNEL',COLS['Layout'])
    MAIN=empty('CacheSeaShallows',COLS['Layout'])
    OUTER_ROOT=empty('CacheSeaShallows_Outer',COLS['Outer'])
    DRIFT_ROOT=empty('CacheSeaShallows_Drift',COLS['Drift'])
    FONT=bpy.data.fonts.load(str(ROOT/'game/Assets/SystemPrompt/Resources/Fonts/NotoSansSC-Bold.ttf'))
    setup_materials();make_kit()
    print('KIT_READY',len(KIT),flush=True)
    meta,exposed=build_layout()
    outer_count=build_outer();build_drift();setup_reference()
    # Text has been converted; remove the file-backed font from the deliverable.
    bpy.data.fonts.remove(FONT)
    stats_and_save(start,meta,exposed,outer_count)

if __name__=='__main__':main()
