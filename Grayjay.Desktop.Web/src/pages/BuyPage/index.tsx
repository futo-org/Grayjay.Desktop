import { type Component } from 'solid-js';
import ScrollContainer from '../../components/containers/ScrollContainer';
import FlexibleArrayList from '../../components/containers/FlexibleArrayList';
import Button from '../../components/buttons/Button';
import { BuyBackend } from '../../backend/BuyBackend';
import UIOverlay from '../../state/UIOverlay';
import { DialogButton, DialogDescriptor, DialogInputText, IDialogOutput } from '../../overlays/OverlayDialog';
import styles from './index.module.css';

import grayjay from '../../assets/grayjay.svg';

const BuyPage: Component = () => {


  function enterLicense() {
    UIOverlay.dialog({
      title: "Enter your License",
      description: "To activate your Grayjay application, enter your license key.",
      input: new DialogInputText("License Key.."),
      buttons: [
        {
          title: "Cancel",
          style: "none",
          onClick() {
                      
          }
        } as DialogButton,
        {
          title: "Confirm",
          style: "primary",
          async onClick(output: IDialogOutput) {
            if(output.text) {
              const result = await BuyBackend.setLicense(output.text);
              if(result) {
                UIOverlay.dialog({
                  title: "Your Grayjay has been activated!",
                  description: "Thanks for purchasing Grayjay, your application has been activated.",
                  buttons: [
                    {
                      title: "You're welcome!",
                      style: "none",
                      onClick() {
                                  
                      }
                    } as DialogButton
                  ]
                } as DialogDescriptor)
              }
              else {
                UIOverlay.dialog({
                  title: "Your license key appears invalid.",
                  description: "Try again and check if there are no mistakes.",
                  buttons: [
                    {
                      title: "Ok",
                      style: "none",
                      onClick() {
                                  
                      }
                    } as DialogButton
                  ]
                } as DialogDescriptor)
              }
            }
          }
        } as DialogButton
      ]
    } as DialogDescriptor)
  }

  function buy(){
    BuyBackend.openBuy();
  }


    return (
    <div style="margin-top: 50px; text-align: center">
        <div style={{'display': 'inline-block', 'font-size': '35px', 'vertical-align': 'top', 'margin-right': '10px'}}>Buy</div>
        <div class={styles.grayjay} style={{'position': 'relative', 'width': '200px', 'display': 'inline-block', 'text-align': 'left'}}>
          <img src={grayjay} />
            <div style="font-size: 35px; top: 2px; left: 60px; position: absolute;">
            Grayjay
            </div>
        </div>
      <p class={styles.description}>
        Grayjay is not an easy or cheap app to build and maintain. We have full time engineers working on the app and its surrounding systems. And will likely not make its money back anytime soon, if ever.<br /><br />FUTO’s mission is for open-source software and non-malicious software business practices to become a sustainable income source for projects and their developers. For this reason we are in favor of users actually paying for the software.<br /><br />That is why Grayjay wants you to pay for the software.
      </p>
      <p class={styles.descriptionShared}>
        License keys are shared between Android and Desktop.
      </p>
      <Button text='Enter License' color='primary' onClick={()=>enterLicense()} style={{margin: '10px'}} />
      <Button text='Buy a License' color="#019BE7" onClick={()=>buy()} style={{margin: '10px'}} />
    </div>
  );
};

export default BuyPage;
