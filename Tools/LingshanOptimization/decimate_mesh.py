import bpy, numpy as np, struct, json, time
from pathlib import Path
import sys
out=Path(sys.argv[sys.argv.index('--')+1])
bpy.ops.wm.read_factory_settings(use_empty=True)
raw=(out/'source-mesh.bin').read_bytes(); nv,ni=struct.unpack_from('<ii',raw)
vertices=np.frombuffer(raw,dtype='<f4',count=nv*8,offset=8).reshape(nv,8)
indices=np.frombuffer(raw,dtype='<i4',count=ni,offset=8+nv*32)
keys=np.round(vertices[:,:3],6)
_,first,remap=np.unique(keys,axis=0,return_index=True,return_inverse=True)
welded=vertices[first,:3]
print('LINGSHAN_WELDED vertices='+str(len(welded)),flush=True)
mesh=bpy.data.meshes.new('Lingshan_Source_LocalSpace'); mesh.vertices.add(len(welded));mesh.vertices.foreach_set('co',welded.ravel())
mesh.loops.add(ni);mesh.loops.foreach_set('vertex_index',remap[indices])
mesh.polygons.add(ni//3);mesh.polygons.foreach_set('loop_start',np.arange(0,ni,3,dtype=np.int32));mesh.polygons.foreach_set('loop_total',np.full(ni//3,3,dtype=np.int32));mesh.polygons.foreach_set('use_smooth',np.ones(ni//3,dtype=bool))
mesh.uv_layers.new(name='UVMap');mesh.uv_layers.active.data.foreach_set('uv',vertices[indices,6:8].ravel());mesh.update()
obj=bpy.data.objects.new('Lingshan_HighDetail_Optimized',mesh);bpy.context.collection.objects.link(obj);bpy.context.view_layer.objects.active=obj;obj.select_set(True)
print('LINGSHAN_MESH_IMPORTED vertices='+str(nv)+' triangles='+str(ni//3),flush=True)
report=[]
for label,count in [('LOD0',60000),('LOD1',15000),('LOD2',3500)]:
 start=time.time(); mod=obj.modifiers.new('PICO Geometry Budget','DECIMATE');mod.ratio=count/len(obj.data.polygons);mod.use_collapse_triangulate=True
 bpy.ops.object.modifier_apply(modifier=mod.name)
 result=obj.data;result.calc_loop_triangles(); result.update()
 exported=[]; face_indices=[];lookup={};uv=result.uv_layers.active.data
 for tri in result.loop_triangles:
  for li in tri.loops:
   vi=result.loops[li].vertex_index; vertex=result.vertices[vi]; u=uv[li].uv
   key=(vi,float(u.x),float(u.y))
   if key not in lookup:
    lookup[key]=len(exported);exported.append(tuple(vertex.co)+tuple(vertex.normal)+(float(u.x),float(u.y)))
   face_indices.append(lookup[key])
 with (out/(label+'.bin')).open('wb') as f:
  f.write(struct.pack('<ii',len(exported),len(face_indices)));f.write(np.array(exported,dtype='<f4').tobytes());f.write(np.array(face_indices,dtype='<i4').tobytes())
 rec={'lod':label,'vertices':len(exported),'triangles':len(face_indices)//3,'seconds':time.time()-start,'localBounds':[list(v) for v in obj.bound_box]};report.append(rec);print('LINGSHAN_DECIMATED '+json.dumps(rec),flush=True)
(out/'lod-generation.json').write_text(json.dumps(report,indent=2));print('LINGSHAN_LODS_EXPORTED',flush=True)
