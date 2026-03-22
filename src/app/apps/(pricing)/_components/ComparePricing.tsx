import React from "react";
import { CheckCircle, XCircle } from "phosphor-react";

const ComparePricing = () => {
  const plans = [
    { name: "Free", features: ["check", "x", "check", "x", "x", "x"] },
    {
      name: "Pro",
      features: ["check", "check", "check", "check", "check", "x"],
    },
    {
      name: "Enterprise",
      features: ["check", "check", "check", "check", "check", "check"],
    },
  ];

  const featureList = [
    "Public",
    "Private",
    "Permissions",
    "Sharing",
    "Unlimited members",
    "Extra security",
  ];

  return (
    <div className="pricing-table table-responsive">
      <table className="table table-bottom-border table-striped align-middle text-center">
        <thead>
          <tr>
            <th></th>
            {plans.map((plan, index) => (
              <th key={index}>{plan.name}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {featureList.map((feature, featureIndex) => (
            <tr key={featureIndex}>
              <th scope="row" className="text-start f-w-500">
                {feature}
              </th>
              {plans.map((plan, planIndex) => (
                <td key={planIndex}>
                  {plan.features[featureIndex] === "check" ? (
                    <CheckCircle
                      size={20}
                      weight="duotone"
                      className="text-success"
                    />
                  ) : (
                    <XCircle
                      size={20}
                      weight="duotone"
                      className="text-danger"
                    />
                  )}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
};

export default ComparePricing;
