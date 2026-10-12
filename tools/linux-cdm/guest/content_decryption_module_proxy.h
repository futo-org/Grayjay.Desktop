

#ifndef CDM_CONTENT_DECRYPTION_MODULE_PROXY_H_
#define CDM_CONTENT_DECRYPTION_MODULE_PROXY_H_

#include "content_decryption_module_export.h"

#if defined(_MSC_VER)
typedef unsigned char uint8_t;
typedef unsigned int uint32_t;
typedef unsigned __int64 uint64_t;
#else
#include <stdint.h>
#endif

namespace cdm {

class CDM_CLASS_API CdmProxyClient;

class CDM_CLASS_API CdmProxy {
 public:
  enum Function : uint32_t {

    kIntelNegotiateCryptoSessionKeyExchange = 1,

  };

  enum KeyType : uint32_t {
    kDecryptOnly = 0,
    kDecryptAndDecode = 1,
  };

  virtual void Initialize() = 0;

  virtual void Process(Function function,
                       uint32_t crypto_session_id,
                       const uint8_t* input_data,
                       uint32_t input_data_size,
                       uint32_t output_data_size) = 0;

  virtual void CreateMediaCryptoSession(const uint8_t* input_data,
                                        uint32_t input_data_size) = 0;

  virtual void SetKey(uint32_t crypto_session_id,
                      const uint8_t* key_id,
                      uint32_t key_id_size,
                      KeyType key_type,
                      const uint8_t* key_blob,
                      uint32_t key_blob_size) = 0;

  virtual void RemoveKey(uint32_t crypto_session_id,
                         const uint8_t* key_id,
                         uint32_t key_id_size) = 0;

 protected:
  CdmProxy() {}
  virtual ~CdmProxy() {}
};

class CDM_CLASS_API CdmProxyClient {
 public:
  enum Status : uint32_t {
    kOk,
    kFail,
  };

  enum Protocol : uint32_t {
    kNone = 0,
    kIntel,

  };

  virtual void OnInitialized(Status status,
                             Protocol protocol,
                             uint32_t crypto_session_id) = 0;

  virtual void OnProcessed(Status status,
                           const uint8_t* output_data,
                           uint32_t output_data_size) = 0;

  virtual void OnMediaCryptoSessionCreated(Status status,
                                           uint32_t crypto_session_id,
                                           uint64_t output_data) = 0;

  virtual void OnKeySet(Status status) = 0;

  virtual void OnKeyRemoved(Status status) = 0;

  virtual void NotifyHardwareReset() = 0;

 protected:
  CdmProxyClient() {}
  virtual ~CdmProxyClient() {}
};

}

#endif
