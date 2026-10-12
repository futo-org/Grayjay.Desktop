// CDM-specific Cygwin private mappings: 64 KiB reservations,
// 4 KiB commits and protections. Empty reservations are released.
#if defined(__CYGWIN__) && defined(BLINK_CYGWIN_LINEAR)
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <errno.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <pthread.h>
#include <sys/mman.h>
#include "blink/vfs.h"
#include "blink/winmap.h"
struct WinRegion { uintptr_t base, end; uint16_t *used; size_t pages; struct WinRegion *next; };
static struct WinRegion *regions;
static pthread_mutex_t region_lock = PTHREAD_MUTEX_INITIALIZER;
static struct WinRegion *FindRegion(uintptr_t addr) {
  struct WinRegion *r;
  for (r=regions;r;r=r->next) if (addr>=r->base && addr<r->end) return r;
  return 0;
}
static uint16_t *RegionMask(struct WinRegion *r,uintptr_t addr) {
  return &r->used[(addr-r->base)>>16];
}
static int RegisterRegion(uintptr_t base,size_t size) {
  struct WinRegion *r=calloc(1,sizeof(*r));
  if(!r)return -1;
  r->used=calloc(size>>16,sizeof(*r->used));
  if(!r->used){free(r);return -1;}
  r->base=base;r->end=base+size;r->next=regions;regions=r;
  return 0;
}
static void ReleaseEmptyRegions(void) {
  struct WinRegion **link=&regions;
  while(*link) {
    struct WinRegion *r=*link;
    if(!r->pages && VirtualFree((void*)r->base,0,MEM_RELEASE)) {
      *link=r->next;free(r->used);free(r);
    } else link=&r->next;
  }
}
static int Decommit(uintptr_t at,size_t size) {
  uintptr_t end=at+size;
  while(at<end) {
    MEMORY_BASIC_INFORMATION info;
    if(!VirtualQuery((void*)at,&info,sizeof(info)))return -1;
    uintptr_t stop=(uintptr_t)info.BaseAddress+info.RegionSize;
    if(stop>end)stop=end;
    if(stop<=at)return -1;
    if(info.State==MEM_COMMIT && !VirtualFree((void*)at,stop-at,MEM_DECOMMIT))return -1;
    at=stop;
  }
  return 0;
}
static DWORD Protection(int prot) {
  if (prot & PROT_EXEC) return prot & PROT_WRITE ? PAGE_EXECUTE_READWRITE : PAGE_EXECUTE_READ;
  if (prot & PROT_WRITE) return PAGE_READWRITE;
  if (prot & PROT_READ) return PAGE_READONLY;
  return PAGE_NOACCESS;
}
int WindowsOwnsMap(void *addr) {
  int yes; pthread_mutex_lock(&region_lock); yes=FindRegion((uintptr_t)addr)!=0;
  pthread_mutex_unlock(&region_lock); return yes;
}
// Used from the SMC signal handler: no allocation or mutex acquisition.
int WindowsRawMprotect(void *addr,size_t size,int prot) {
  uintptr_t at=(uintptr_t)addr,end=at+size; DWORD old;
  while(at<end) {
    size_t n=65536-(at&65535);if(n>end-at)n=end-at;
    if(!VirtualProtect((void*)at,n,Protection(prot),&old)) {errno=ENOMEM;return -1;}
    at+=n;
  }
  return 0;
}
int WindowsMprotect(void *addr,size_t size,int prot) {
  uintptr_t at=(uintptr_t)addr,end=at+((size+4095)&~(size_t)4095); DWORD old;
  pthread_mutex_lock(&region_lock);
  for(uintptr_t p=at;p<end;) {
    struct WinRegion *r=FindRegion(p);size_t n=65536-(p&65535);if(n>end-p)n=end-p;
    unsigned first=(p&65535)/4096,count=n/4096,mask=((1u<<count)-1)<<first;
    if(!r || (*RegionMask(r,p)&mask)!=mask) {pthread_mutex_unlock(&region_lock);errno=ENOMEM;return -1;}
    p+=n;
  }
  pthread_mutex_unlock(&region_lock);
  for(;at<end;) {
    size_t n=65536-(at&65535); if(n>end-at)n=end-at;
    if(prot) {
      if(!VirtualAlloc((void*)at,n,MEM_COMMIT,Protection(prot)) ||
         !VirtualProtect((void*)at,n,Protection(prot),&old)) {errno=ENOMEM;return -1;}
    } else {
      uintptr_t p=at;
      while(p<at+n) {
        MEMORY_BASIC_INFORMATION info;
        if(!VirtualQuery((void*)p,&info,sizeof(info))) {errno=ENOMEM;return -1;}
        uintptr_t e=(uintptr_t)info.BaseAddress+info.RegionSize;if(e>at+n)e=at+n;
        if(info.State==MEM_COMMIT && !VirtualProtect((void*)p,e-p,PAGE_NOACCESS,&old)) {errno=ENOMEM;return -1;}
        p=e;
      }
    }
    at+=n;
  }
  return 0;
}
int WindowsMunmap(void *addr,size_t size) {
  uintptr_t at=(uintptr_t)addr,end=at+((size+4095)&~(size_t)4095);
  pthread_mutex_lock(&region_lock);
  for(;at<end;) {
    struct WinRegion *r=FindRegion(at); size_t n=65536-(at&65535); if(n>end-at)n=end-at;
    if(r) {
      if(Decommit(at,n)) {pthread_mutex_unlock(&region_lock);errno=ENOMEM;return -1;}
      unsigned first=(at&65535)/4096,count=n/4096,mask=((1u<<count)-1)<<first;
      r->pages-=__builtin_popcount(*RegionMask(r,at)&mask);
      *RegionMask(r,at) &= ~mask;
    }
    at+=n;
  }
  ReleaseEmptyRegions();
  pthread_mutex_unlock(&region_lock);return 0;
}
void *WindowsMmap(void *addr,size_t size,int prot,int flags,int fd,off_t offset) {
  uintptr_t at,end,base,start; size_t n,index=0; void *reserved;
  uint16_t *old=0;int mutating=0;
  if(!size || ((uintptr_t)addr&4095) || (offset&4095)) {errno=EINVAL;return MAP_FAILED;}
  if(flags&MAP_SHARED) {errno=ENOTSUP;return MAP_FAILED;}
  if(size>SIZE_MAX-65535 || (uintptr_t)addr>UINTPTR_MAX-size-65535) {errno=EINVAL;return MAP_FAILED;}
  size=(size+4095)&~(size_t)4095;
  errno=0;
  pthread_mutex_lock(&region_lock);
  if(!addr) {
    n=(size+65535)&~(size_t)65535;
    // Avoid low addresses: Blink intentionally excludes the first 2 MiB
    // from reverse guest-address lookup and SMC fault handling.
    reserved=VirtualAlloc(0,n,MEM_RESERVE|MEM_TOP_DOWN,PAGE_NOACCESS);
    if(!reserved)goto failed;
    addr=reserved;
    if(RegisterRegion((uintptr_t)addr,n)){VirtualFree(addr,0,MEM_RELEASE);goto failed;}
  }
  start=(uintptr_t)addr;
  end=start+size;
  old=calloc(((start&65535)+size+65535)>>16,sizeof(*old));
  if(!old)goto failed;
  // Reserve only addresses we own; never overwrite the host or Cygwin.
  for(at=(uintptr_t)addr;at<end;) {
    struct WinRegion *r=FindRegion(at);base=at&~(uintptr_t)65535;
    if(!r) {
      uintptr_t stop=(end+65535)&~(uintptr_t)65535;
      for(struct WinRegion *other=regions;other;other=other->next)
        if(other->base>base && other->base<stop)stop=other->base;
      size_t span=stop-base;
      reserved=VirtualAlloc((void*)base,span,MEM_RESERVE,PAGE_NOACCESS);
      if(reserved!=(void*)base)goto failed;
      if(RegisterRegion(base,span)){VirtualFree((void*)base,0,MEM_RELEASE);goto failed;}
      r=FindRegion(at);
    }
    n=65536-(at&65535);if(n>end-at)n=end-at;
    unsigned first=(at&65535)/4096,count=n/4096,mask=((1u<<count)-1)<<first;
    if((*RegionMask(r,at)&mask) && !(flags&MAP_FIXED)) {errno=EEXIST;goto failed;}
    old[index++]=*RegionMask(r,at)&mask;
    at+=n;
  }
  index=0;mutating=1;
  for(at=(uintptr_t)addr;at<end;) {
    struct WinRegion *r=FindRegion(at);n=65536-(at&65535);if(n>end-at)n=end-at;
    if(prot || fd!=-1) {
      if(!VirtualAlloc((void*)at,n,MEM_COMMIT,PAGE_READWRITE))goto failed;
      DWORD previous;
      if(!VirtualProtect((void*)at,n,PAGE_READWRITE,&previous))goto failed;
    } else if(old[index] && Decommit(at,n))goto failed;
    unsigned first=(at&65535)/4096,count=n/4096,mask=((1u<<count)-1)<<first;
    r->pages+=__builtin_popcount(mask&~*RegionMask(r,at));
    *RegionMask(r,at) |= mask;at+=n;++index;
  }
  if(prot || fd!=-1) {
    if(fd!=-1) {
      // Read directly into the staged writable mapping. Avoid a second
      // CDM-sized buffer; rollback releases new pages on a failed read.
      size_t done=0;
      while(done<size) {
        ssize_t got=VfsPread(fd,(char*)addr+done,size-done,offset+done);
        if(got<0){if(errno==EINTR)continue;goto failed;}
        if(!got)break;
        done+=got;
      }
      if(done<size)memset((char*)addr+done,0,size-done);
    } else {
      index=0;
      for(at=start;at<end;) {
        n=65536-(at&65535);if(n>end-at)n=end-at;
        if(old[index])memset((void*)at,0,n);
        at+=n;++index;
      }
    }
    for(at=start;at<end;) {
      n=65536-(at&65535);if(n>end-at)n=end-at;
      DWORD previous;
      if(!VirtualProtect((void*)at,n,Protection(prot),&previous))goto failed;
      at+=n;
    }
  }
  free(old);
  pthread_mutex_unlock(&region_lock);
  return addr;
failed:
  // Undo newly installed pages; preserve neighboring mappings in shared
  // reservation blocks. MAP_FIXED replacement may discard the replaced data.
  if(old && mutating) {
    index=0;
    for(at=start;at<end;) {
      struct WinRegion *r=FindRegion(at);n=65536-(at&65535);if(n>end-at)n=end-at;
      unsigned first=(at&65535)/4096,count=n/4096,mask=((1u<<count)-1)<<first;
      if(r) {
        unsigned fresh=mask&~old[index];
        for(unsigned j=first;j<first+count;) {
          if(!(fresh&(1u<<j))){++j;continue;}
          unsigned begin=j;while(j<first+count && (fresh&(1u<<j)))++j;
          Decommit((at&~(uintptr_t)65535)+begin*4096,(j-begin)*4096);
        }
        unsigned before=*RegionMask(r,at);
        unsigned after=(before&~mask)|old[index];
        r->pages-=__builtin_popcount(before&mask);
        r->pages+=__builtin_popcount(after&mask);
        *RegionMask(r,at)=after;
      }
      at+=n;++index;
    }
  }
  ReleaseEmptyRegions();free(old);
  pthread_mutex_unlock(&region_lock);if(errno!=EEXIST)errno=ENOMEM;return MAP_FAILED;
}

#endif
