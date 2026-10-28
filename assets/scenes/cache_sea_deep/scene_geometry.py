"""Copied shallow-scene primitives; headless, pure-colour geometry only.

No runtime imports from the shallow asset directory. One Blender unit is one metre.
"""
from pathlib import Path
import bpy, math, random, json
import numpy as np
from mathutils import Vector, Matrix
BASE=Path(__file__).resolve().parent
ROOT=BASE.parents[2]
TAU=math.tau
MATS={}
COLS={}
CLIPPED={}

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
    def box(self, at, size, mat, sides=(0,1,2,3,4,5), rot=0, bevel=None):
        """A box; sides 0-5 are -z, -y, +x, +y, -x, +z. Every box at least 25 cm thick with all four walls and
        a top has its edges cut back at 45 degrees (bevel, by default 6% of its thinnest side, 3-15 cm), so no
        chunk of the scene is a raw cube: the edges catch the light and the outline the way a made thing's do.
        A chamfered box leaves out only its bottom face when asked."""
        x,y,z=at; a,b,c=[v/2 for v in size]
        cr,sr=math.cos(rot),math.sin(rot)
        put=lambda u,v,w:(x+u*cr-v*sr,y+u*sr+v*cr,z+w)
        if bevel is None:
            bevel=min(.15,max(.03,min(size)*.06)) if min(size)>=.25 and {1,2,3,4,5}<=set(sides) else 0
        if bevel<=0:
            cs=[(-a,-b,-c),(a,-b,-c),(a,b,-c),(-a,b,-c),(-a,-b,c),(a,-b,c),(a,b,c),(-a,b,c)]
            vs=[put(*p) for p in cs]
            fs=[(0,3,2,1),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7),(4,5,6,7)]
            for i in sides:self.face([vs[j] for j in fs[i]],mat)
            return
        h=(a,b,c);e=min(bevel,min(h)*.9)
        def out(points,normal):
            # Wound so the face points along normal (counter-clockwise seen from outside).
            p=[Vector(q) for q in points]
            if (p[1]-p[0]).cross(p[2]-p[0]).dot(Vector(normal))<0:points=list(reversed(points))
            self.face([put(*q) for q in points],mat)
        def pt(k,i,j,vk,vi,vj):
            q=[0,0,0];q[k]=vk;q[i]=vi;q[j]=vj;return tuple(q)
        for k in range(3):
            i,j=[n for n in range(3) if n!=k]
            for s in (-1,1):
                if k==2 and s==-1 and 0 not in sides:continue
                n=[0,0,0];n[k]=s
                out([pt(k,i,j,s*h[k],u*(h[i]-e),v*(h[j]-e)) for u,v in ((-1,-1),(1,-1),(1,1),(-1,1))],n)
        for k in range(3):
            i,j=[n for n in range(3) if n!=k]
            for si in (-1,1):
                for sj in (-1,1):
                    n=[0,0,0];n[i]=si;n[j]=sj
                    out([pt(k,i,j,-(h[k]-e),si*h[i],sj*(h[j]-e)),pt(k,i,j,h[k]-e,si*h[i],sj*(h[j]-e)),
                         pt(k,i,j,h[k]-e,si*(h[i]-e),sj*h[j]),pt(k,i,j,-(h[k]-e),si*(h[i]-e),sj*h[j])],n)
        for sx in (-1,1):
            for sy in (-1,1):
                for sz in (-1,1):
                    out([(sx*a,sy*(b-e),sz*(c-e)),(sx*(a-e),sy*b,sz*(c-e)),(sx*(a-e),sy*(b-e),sz*c)],(sx,sy,sz))
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
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                region=area.spaces.active.region_3d
                region.view_location=(58,46,0);region.view_distance=155
                region.view_rotation=(1,0,0,0);region.view_perspective='ORTHO'
                area.spaces.active.shading.color_type='MATERIAL'
