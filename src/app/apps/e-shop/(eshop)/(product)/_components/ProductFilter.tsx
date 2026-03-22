import React, { useState } from "react";
import {
  Accordion,
  AccordionBody,
  AccordionHeader,
  AccordionItem,
  Button,
  Input,
} from "reactstrap";
import { Dress } from "@phosphor-icons/react";
import {
  CirclesThreePlus,
  DesktopTower,
  FirstAidKit,
  GameController,
  ShoppingCart,
} from "phosphor-react";
import { IconStar, IconStarFilled } from "@tabler/icons-react";

const FilterSidebar = () => {
  const [open, setOpen] = useState<string[]>([]);

  const toggle = (id: string) => {
    setOpen((prev) =>
      prev.includes(id) ? prev.filter((item) => item !== id) : [...prev, id]
    );
  };

  const [selected, setSelected] = useState<string>("primary");

  const options = [
    { value: "primary", className: "check-primary" },
    { value: "secondary", className: "check-secondary" },
    { value: "success", className: "check-success" },
    { value: "danger", className: "check-danger" },
    { value: "warning", className: "check-warning" },
    { value: "info", className: "check-info" },
    { value: "light", className: "check-light" },
    { value: "dark", className: "check-dark" },
  ];

  const [checkedItems, setCheckedItems] = useState<string[]>([]);

  const checkeOptions = ["Men", "Women", "Boys", "Girls", "Boys & Girls"];

  const handleToggle = (value: string) => {
    setCheckedItems((prev) =>
      prev.includes(value)
        ? prev.filter((item) => item !== value)
        : [...prev, value]
    );
  };

  const [selectedOption, setSelectedOption] = useState("Featured");

  const rating = 3;

  return (
    <div className="p-0">
      <Accordion
        className="accordion accordion-flush app-accordion"
        flush
        open={open}
        toggle={toggle}
      >
        {/* Sort By */}
        <AccordionItem>
          <AccordionHeader targetId="1" className="f-w-600">
            Sort By
          </AccordionHeader>
          <AccordionBody accordionId="1" className="p-0 collapse show">
            <div>
              {[
                "Featured",
                "Price: High to Low",
                "Price: Low to High",
                "Newest",
                "Ratings",
              ].map((option) => (
                <label key={option} className="check-box ms-2 mb-3">
                  <input
                    type="radio"
                    name="radio-group1"
                    value={option}
                    checked={selectedOption === option}
                    onChange={() => setSelectedOption(option)}
                  />
                  <span className="radiomark outline-secondary"></span>
                  <span className="text-secondary f-w-600">{option}</span>
                </label>
              ))}
            </div>
          </AccordionBody>
        </AccordionItem>

        {/* Categories */}
        <AccordionItem>
          <AccordionHeader targetId="2" className="f-w-600">
            Categories
          </AccordionHeader>
          <AccordionBody accordionId="2" className="collapse show">
            <div>
              {[
                { icon: <Dress size={18} />, label: "Fashion" },
                { icon: <DesktopTower size={18} />, label: "Home Appliances" },
                { icon: <FirstAidKit size={18} />, label: "Health & Beauty" },
                { icon: <GameController size={18} />, label: "Toys & Games" },
                { icon: <ShoppingCart size={18} />, label: "Groceries" },
                { icon: <CirclesThreePlus size={18} />, label: "See all" },
              ].map((item, index) => (
                <div
                  key={index}
                  className=" d-flex align-items-center gap-2 mb-3"
                >
                  <label className="check-box mb-0">
                    <input type="checkbox" />
                    <span className="checkmark outline-secondary ms-2"></span>
                  </label>
                  <a
                    href="#"
                    className="f-s-15 f-w-500 text-secondary d-flex align-items-center"
                  >
                    <span className="text-dark me-2">{item.icon}</span>
                    {item.label}
                  </a>
                </div>
              ))}
            </div>
          </AccordionBody>
        </AccordionItem>

        {/* Color */}
        <AccordionItem>
          <AccordionHeader targetId="3">Color</AccordionHeader>
          <AccordionBody accordionId="3" className="collapse show">
            <div className="d-flex flex-wrap">
              {options.map((option) => (
                <label key={option.value} className="check-box">
                  <Input
                    type="radio"
                    name="radio-groupbox"
                    value={option.value}
                    checked={selected === option.value}
                    onChange={() => setSelected(option.value)}
                  />
                  <span
                    className={`radiomark ${option.className} ms-1 w-25 h-25`}
                  ></span>
                </label>
              ))}
            </div>
          </AccordionBody>
        </AccordionItem>

        {/* Gender */}
        <AccordionItem>
          <AccordionHeader targetId="4">Gender</AccordionHeader>
          <AccordionBody accordionId="4" className="collapse show">
            <div className="d-flex flex-column gap-2">
              {checkeOptions.map((option) => (
                <label
                  key={option}
                  className="check-box d-flex align-items-center"
                >
                  <Input
                    type="checkbox"
                    checked={checkedItems.includes(option)}
                    onChange={() => handleToggle(option)}
                  />
                  <span className="checkmark outline-secondary"></span>
                  <span className="text-secondary ms-2 f-s-16 f-w-500">
                    {option}
                  </span>
                </label>
              ))}
            </div>
          </AccordionBody>
        </AccordionItem>

        {/* Customer Ratings */}
        <AccordionItem>
          <AccordionHeader targetId="5">Customer Ratings</AccordionHeader>
          <AccordionBody accordionId="5" className="collapse show">
            <div className="rating justify-content-end d-flex">
              {[...Array(5)].map((_, index) =>
                index < rating ? (
                  <IconStarFilled key={index} className="f-s-18 text-warning" />
                ) : (
                  <IconStar key={index} className="f-s-18 text-secondary" />
                )
              )}
            </div>
          </AccordionBody>
        </AccordionItem>

        {/* Price Range */}
        <AccordionItem>
          <AccordionHeader targetId="6">Price Range</AccordionHeader>
          <AccordionBody accordionId="6" className="collapse show">
            <div className="d-flex flex-column gap-2">
              <input type="range" className="form-range" />
              <div className="d-flex gap-2">
                <Input
                  type="number"
                  className="form-control"
                  placeholder="Min"
                />
                <Input
                  type="number"
                  className="form-control"
                  placeholder="Max"
                />
              </div>
            </div>
          </AccordionBody>
        </AccordionItem>
      </Accordion>

      {/* Clear & Apply Buttons */}
      <div className="text-end m-3">
        <Button size="sm" color="primary" className="me-2">
          Clear All
        </Button>
        <Button size="sm" color="secondary">
          Apply
        </Button>
      </div>
    </div>
  );
};

export default FilterSidebar;
