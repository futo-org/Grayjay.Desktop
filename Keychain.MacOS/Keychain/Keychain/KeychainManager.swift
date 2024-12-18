//
//  KeychainManager.swift
//  Keychain
//
//  Created by Koen on 01/03/2024.
//

import Foundation
import Security

public class KeychainManager {
    public static func saveKey(keyName: String, key: Data) -> Int {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrAccount as String: keyName,
            kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlock
        ]
        
        let keychainItem: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlock,
            kSecAttrAccount as String: keyName,
            kSecValueData as String: key
        ]
        
        let statusUpdate = SecItemUpdate(query as CFDictionary, keychainItem as CFDictionary)
        
        if statusUpdate == errSecItemNotFound {
            let statusAdd = SecItemAdd(keychainItem as CFDictionary, nil)
            if (statusAdd != 0) {
                return Int(statusAdd)
            }
        } else if (statusUpdate != errSecSuccess) {
            return Int(statusUpdate)
        }
        
        return 0
    }
    
    public static func retrieveKey(keyName: String) -> Data? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrAccount as String: keyName,
            kSecReturnData as String: kCFBooleanTrue!,
            kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlock
        ]
        
        var item: AnyObject?
        let status = SecItemCopyMatching(query as CFDictionary, &item)
        if status == errSecSuccess {
            return item as? Data
        } else {
            return nil
        }
    }
    
    public static func deleteKey(keyName: String) -> Int {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrAccount as String: keyName,
            kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlock
        ]
        
        let status = SecItemDelete(query as CFDictionary)
        if status != errSecSuccess {
            return Int(status)
        }
        
        return 0
    }
}

@_cdecl("SaveKey")
public func SaveKey(keyName: UnsafePointer<CChar>, key: UnsafePointer<UInt8>, keySize: Int) -> Int {
    let keyNameString = String(cString: keyName)
    let keyData = Data(bytes: key, count: keySize)
    return KeychainManager.saveKey(keyName: keyNameString, key: keyData)
}

@_cdecl("RetrieveKey")
public func RetrieveKey(keyName: UnsafePointer<CChar>, length: UnsafeMutablePointer<Int>) -> UnsafePointer<UInt8>? {
    let keyNameString = String(cString: keyName)
    if let keyData = KeychainManager.retrieveKey(keyName: keyNameString) {
        let keyBytes = UnsafeMutablePointer<UInt8>.allocate(capacity: keyData.count)
        keyData.copyBytes(to: keyBytes, count: keyData.count)
        length.pointee = keyData.count
        return UnsafePointer<UInt8>(keyBytes)
    } else {
        length.pointee = 0
        return nil
    }
}

@_cdecl("DeleteKey")
public func DeleteKey(keyName: UnsafePointer<CChar>) -> Int {
    let keyNameString = String(cString: keyName)
    return KeychainManager.deleteKey(keyName: keyNameString)
}

@_cdecl("FreeKey")
public func FreeKey(key: UnsafePointer<UInt8>?) {
    key?.deallocate()
}
