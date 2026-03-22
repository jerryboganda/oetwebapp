import React, { useState } from "react";
import { Card, CardBody } from "reactstrap";
import CheckoutTabsData from "@/app/apps/e-shop/(eshop)/(checkout)/_components/CheckoutTabsData";
import {
  IconUserCircle,
  IconDisc,
  IconUserPlus,
  IconCurrencyDollar,
} from "@tabler/icons-react";

const CheckoutTabs = () => {
  const [currentStep, setCurrentStep] = useState(1);

  const steps = [
    { id: 1, title: "Billing Info", icon: <IconUserCircle size={20} /> },
    { id: 2, title: "Address", icon: <IconDisc size={20} /> },
    { id: 3, title: "Payment", icon: <IconUserPlus size={20} /> },
    { id: 4, title: "Review", icon: <IconCurrencyDollar size={20} /> },
    { id: 5, title: "Finish", icon: <IconDisc size={20} /> },
  ];

  const handleStepClick = (stepId: number) => {
    setCurrentStep(stepId);
  };

  return (
    <>
      <Card>
        <CardBody>
          <div className="checkout-tabs-step">
            {steps.map((step) => (
              <div
                key={step.id}
                className={`tab d-flex align-items-center ${
                  currentStep === step.id ? "checkout-current-step" : ""
                }`}
                onClick={() => handleStepClick(step.id)}
                role="button"
              >
                <div className="tabs-steps">{step.icon}</div>
                <div className="px-2">
                  <h6 className="mb-0">{step.title}</h6>
                  <span className="text-secondary">Step {step.id}</span>
                </div>
              </div>
            ))}
          </div>
        </CardBody>
      </Card>

      <Card>
        <CardBody>
          <CheckoutTabsData currentStep={currentStep} />
        </CardBody>
      </Card>
    </>
  );
};

export default CheckoutTabs;
