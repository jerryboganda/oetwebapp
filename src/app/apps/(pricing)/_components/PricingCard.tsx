import React from "react";
import { Col } from "reactstrap";
interface PricingCardProps {
  plan: string;
  price: number;
  yearlyPrice: number;
  features: string[];
  imgSrc: string;
  imgSrcBack: string;
  isBest?: boolean;
  bgColor: string;
}
const PricingCard: React.FC<PricingCardProps> = ({
  plan,
  price,
  yearlyPrice,
  features,
  imgSrc,
  imgSrcBack,
  isBest = false, // Default value
  bgColor,
}) => {
  return (
    <Col xl={3}>
      <div className="flip-pricing">
        <div className="flip-card">
          <div className="front-pricing card">
            {isBest && (
              <div className="box-ribbon box-right">
                <div className={`ribbonbox box-right ribbon-${bgColor}`}>
                  Best
                </div>
              </div>
            )}
            <div className="pricing-header">
              <h6 className="f-w-500 text-dark">{plan}</h6>
              <img src={imgSrc} className="w-60 h-60 rounded" alt="" />
            </div>
            <div className="app-divider-v justify-content-center m-0">
              <p className={`price f-w-700 text-${bgColor}`}>
                ${price}
                <span className="f-w-500 f-s-12">/per Month</span>
              </p>
            </div>
            <div className="pricing-content">
              <ul className="pricing-list">
                {features.map((feature, index) => (
                  <li key={index}>{feature}</li>
                ))}
              </ul>
              <button type="button" className={`btn btn-${bgColor} w-100`}>
                Buy Plans
              </button>
            </div>
          </div>
          <div className="back-pricing card">
            <div className="pricing-header">
              <h6 className="f-w-500 text-dark">{plan}</h6>
              <img src={imgSrcBack} className="w-60 h-60 rounded" alt="" />
            </div>
            <div className="app-divider-v justify-content-center m-0">
              <p className={`price f-w-700 text-${bgColor}`}>
                ${yearlyPrice}
                <span className="f-w-500 f-s-12">/per Yearly</span>
              </p>
            </div>
            <div className="pricing-content">
              <ul className="pricing-list">
                {features.map((feature, index) => (
                  <li key={index}>{feature}</li>
                ))}
              </ul>
              <button type="button" className={`btn btn-${bgColor} w-100`}>
                Buy Plans
              </button>
            </div>
          </div>
        </div>
      </div>
    </Col>
  );
};

export default PricingCard;
