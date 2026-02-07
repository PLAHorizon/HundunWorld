using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core
{

    /// <summary>
    /// 自定义数据注解验证
    /// </summary>
    public class CustomValidateAttribute : ValidationAttribute
    {
        private readonly int maxWord;
        private static int Max;
        public CustomValidateAttribute(int number) : base(errorMessage: $"{number}超过了限定值{Max}")
        {
            maxWord = number;
            Max = number;
        }


        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            // return base.IsValid(value, validationContext);
            if (value != null)
            {
                if ((int)value > maxWord)
                {
                    return new ValidationResult(FormatErrorMessage(validationContext.DisplayName));
                }
            }
            return ValidationResult.Success;
        }
    }
}
