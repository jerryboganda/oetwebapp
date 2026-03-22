import React from "react";
import { DewPoint } from "iconoir-react";
import { weatherData } from "@/Data/HeaderMenuData";
import { IconDropletFilled } from "@tabler/icons-react";
import { Offcanvas, OffcanvasBody } from "reactstrap";

const HeaderCloud: React.FC = () => {
  const [isOpen, setIsOpen] = React.useState(false);

  const toggleCanvas = () => setIsOpen(!isOpen);

  return (
    <>
      <a
        color="link"
        href="#"
        className="head-icon"
        onClick={toggleCanvas}
        aria-controls="cloudoffcanvasTops"
        aria-expanded={isOpen}
      >
        <DewPoint height={26} width={26} className="text-primary f-s-26 me-1" />
        <span className="f-w-600">
          26 <sup className="f-s-10">°C</sup>
        </span>
      </a>

      <Offcanvas
        direction="end"
        isOpen={isOpen}
        toggle={toggleCanvas}
        className="header-cloud-canvas"
        id="cloudoffcanvasTops"
      >
        <OffcanvasBody className="p-0">
          <div className="cloud-body">
            <div className="cloud-content-box">
              {weatherData.map((data, index) => {
                const Icon = data.icon;
                return (
                  <div className={`cloud-box ${data.bgClass}`} key={index}>
                    <p className="mb-3">{data.day}</p>
                    <h6 className="mt-4 f-s-13">{data.temperature}</h6>
                    <span>
                      <Icon className="text-white f-s-25" />
                    </span>
                    <p className="f-s-13 d-flex align-items-center justify-content-center mt-3">
                      <IconDropletFilled size={12} className="me-1" />
                      {data.rain}
                    </p>
                  </div>
                );
              })}
            </div>
          </div>
        </OffcanvasBody>
      </Offcanvas>
    </>
  );
};

export default HeaderCloud;
